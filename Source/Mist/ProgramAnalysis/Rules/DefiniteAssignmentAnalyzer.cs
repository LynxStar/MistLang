using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Reads of what nothing has yet assigned, on the assigned-set lattice: intersection at a join, a
/// declaration without an initializer removing its local, an assignment adding its target, and a read
/// adding the local it read, so that one unassigned local is one complaint, at its first read on each
/// path, rather than one per use. A local is tracked only when declared without an initializer, since
/// every other way a local comes to be gives it a value. A field is tracked only inside a constructor of
/// its own type that does not chain to
/// `this(...)`, when its type has no default and is not a type parameter, whose default is each
/// instantiation's to decide; and only as classic tracks it: read through an explicit `this`, so that
/// a bare read, a call on `this`, or `this` handed away stays admitted as classic admits it -- the
/// decision is recorded on order 10 of the register, with the spec's stricter wording, for the
/// language's owner.
/// </summary>
internal sealed class DefiniteAssignmentAnalyzer : IProgramAnalyzer
{
    public string Id => "flow-framework";

    public void Analyze(AnalysisContext context)
    {
        foreach(var root in SourceBodies.Of(context.Model))
        {
            var constructor = root.Member as Constructor;
            var storage = constructor is { ChainKind: not ConstructorChainKind.This, DeclaringType: ConcreteType owner }
                && ReferenceEquals(root.Root, constructor.Body)
                ? Unassigned(owner)
                : [];

            var tracked = BodyWalker.DescendantsAndSelf(root.Root)
                .OfType<LocalDeclarationStatement>()
                .Where(declaration => declaration.Initializer is null)
                .Select(declaration => (object)declaration.Local)
                .ToHashSet(ReferenceEqualityComparer.Instance);

            var flow = new DefiniteAssignment(context, tracked, storage, constructor, LocalFunctionReads.Of(root.Root));
            var solution = flow.Judge(FlowGraphBuilder.Build(root.Root), DefiniteAssignment.Nothing);

            if(constructor is null || !solution.ReachesExit)
            {
                continue;
            }

            foreach(var member in storage.Where(member => !solution.AtExit.Contains(member)))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.FieldNotDefinitelyAssigned, constructor.DeclarationNode!, [],
                    NameOf(member), constructor.Name, TypeDisplay.Mist(TypeOf(member)));
            }
        }
    }

    private static List<object> Unassigned(ConcreteType owner)
    {
        var storage = new List<object>();

        foreach(var field in Defaultability.InstanceFields(owner))
        {
            if(field.Initializer is null && !Undecided(field.Type) && !Defaultability.FieldHasDefault(owner, field))
            {
                storage.Add(field);
            }
        }

        foreach(var property in owner.PropertiesList)
        {
            if(property is { Modifiers.IsStatic: false, Initializer: null, Getter.IsAuto: true }
                && !Undecided(property.Type) && !Defaultability.HasDefault(property.Type))
            {
                storage.Add(property);
            }
        }

        return storage;
    }

    private static bool Undecided(IMistType type)
    {
        return type is TypeParameter or SpreadType or PackElementType
            || (type is TupleType tuple && tuple.ElementTypes.Any(Undecided));
    }

    private static string NameOf(object member)
    {
        return member switch
        {
            Field field => field.Name,
            Property property => property.Name,
            LocalVariable local => local.Name,
            _ => throw new InvalidOperationException($"ICE: {member.GetType().Name} is not storage definite assignment tracks."),
        };
    }

    private static IMistType TypeOf(object member)
    {
        return member switch
        {
            Field field => field.Type,
            Property property => property.Type,
            _ => throw new InvalidOperationException($"ICE: {member.GetType().Name} is not storage definite assignment tracks."),
        };
    }

    private sealed class DefiniteAssignment(
        AnalysisContext context,
        IReadOnlySet<object> tracked,
        IReadOnlyList<object> storage,
        Constructor? constructor,
        IReadOnlyDictionary<Method, IReadOnlySet<LocalVariable>> functionReads) : IFlowLattice<ImmutableHashSet<object>>
    {
        public static readonly ImmutableHashSet<object> Nothing = ImmutableHashSet.Create<object>(ReferenceEqualityComparer.Instance);

        public ImmutableHashSet<object> Join(ImmutableHashSet<object> left, ImmutableHashSet<object> right)
        {
            return left.Intersect(right);
        }

        public bool Same(ImmutableHashSet<object> left, ImmutableHashSet<object> right)
        {
            return left.SetEquals(right);
        }

        public ImmutableHashSet<object> Transfer(ImmutableHashSet<object> state, FlowStep step)
        {
            return step switch
            {
                { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: null } declaration }
                    => state.Remove(declaration.Local),
                { Kind: FlowStepKind.Declare, Variable: { } declared } => state.Add(declared),
                { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }
                    when Written(assignment.Target) is { } written => state.Add(written),
                { Kind: FlowStepKind.Evaluate, Node: LocalReferenceExpression read } when tracked.Contains(read.Local)
                    => state.Add(read.Local),
                { Kind: FlowStepKind.Evaluate } when Entered(step.Node) is { } reads
                    => state.Union(reads.Where(tracked.Contains)),
                _ => state,
            };
        }

        private IReadOnlySet<LocalVariable>? Entered(object node)
        {
            return LocalFunctionReads.Entered(node) is { } function && functionReads.TryGetValue(function, out var reads)
                ? reads
                : null;
        }

        public FlowSolution<ImmutableHashSet<object>> Judge(FlowGraph graph, ImmutableHashSet<object> entry)
        {
            var solution = FlowSolution<ImmutableHashSet<object>>.Solve(graph, this, entry);
            solution.Replay((state, step) => Judge(graph, state, step));
            return solution;
        }

        private void Judge(FlowGraph graph, ImmutableHashSet<object> state, FlowStep step)
        {
            if(step.Kind != FlowStepKind.Evaluate)
            {
                return;
            }

            switch(step.Node)
            {
                case LocalReferenceExpression reference when tracked.Contains(reference.Local) && !state.Contains(reference.Local):
                    context.Report(ProgramAnalysisDiagnostics.LocalReadBeforeAssignment, reference.Node, [reference], reference.Local.Name);
                    break;

                case MemberAccessExpression { Receiver: ThisExpression } access
                    when Storage(access.Member) is { } member && storage.Contains(member) && !state.Contains(member):
                    context.Report(
                        ProgramAnalysisDiagnostics.FieldReadBeforeAssignment, access.Node, [access],
                        NameOf(member), constructor!.Name);
                    break;

                case LambdaExpression or AsyncBlockExpression when graph.Nested.TryGetValue(step.Node, out var nested):
                    _ = new DefiniteAssignment(context, tracked, [], constructor: null, functionReads).Judge(nested, state);
                    break;

                // Entered with every tracked local taken as assigned, because a captured read is judged
                // at each entry into the function rather than inside it -- see LocalFunctionReads -- and
                // the body's own declarations remove their locals again as they are reached.
                case LocalFunctionStatement when graph.Nested.TryGetValue(step.Node, out var nested):
                    _ = new DefiniteAssignment(context, tracked, [], constructor: null, functionReads).Judge(nested, state.Union(tracked));
                    break;

                case InvocationExpression or FunctionReferenceExpression
                    when LocalFunctionReads.Entered(step.Node) is { } function && functionReads.TryGetValue(function, out var reads):
                    foreach(var local in reads.Where(local => tracked.Contains(local) && !state.Contains(local)))
                    {
                        context.Report(
                            ProgramAnalysisDiagnostics.LocalFunctionReadsUnassigned, ((Expression)step.Node).Node,
                            [(Expression)step.Node], function.Name, local.Name);
                    }

                    break;
            }
        }

        private static object? Written(Expression target)
        {
            return target switch
            {
                SpreadExpression spread => Written(spread.Operand),
                LocalReferenceExpression local => local.Local,
                FieldReferenceExpression field => field.Field,
                PropertyReferenceExpression property => property.Property,
                MemberAccessExpression { Receiver: ThisExpression } access => Storage(access.Member),
                MemberAccessExpression { Receiver: LocalReferenceExpression local } when FlowGraphBuilder.IsValueLocal(local) => local.Local,
                _ => null,
            };
        }

        private static object? Storage(LookupResult? member)
        {
            return member switch
            {
                FieldResult field => field.Field,
                PropertyResult property => property.Property,
                _ => null,
            };
        }
    }
}