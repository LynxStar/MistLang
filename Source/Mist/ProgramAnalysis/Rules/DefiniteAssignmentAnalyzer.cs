using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Reads of what nothing has yet assigned, on the assigned-set lattice: intersection at a join, a
/// declaration without an initializer removing its slots, an assignment adding its target slots, and a
/// read settling only the slots it needs, so repeated reads of the same unassigned path do not cascade.
/// A local is tracked when declared without an initializer or bound by a pattern, whose Bind step
/// assigns its slots only on the successful path. A field is tracked only inside a constructor of
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
        foreach(var root in context.Bodies)
        {
            var constructor = root.Member as Constructor;
            var storage = constructor is { ChainKind: not ConstructorChainKind.This, DeclaringType: ConcreteType owner }
                && ReferenceEquals(root.Root, constructor.Body)
                ? Unassigned(owner)
                : [];

            var tracked = BodyWalker.DescendantsAndSelf(root.Root)
                .OfType<LocalDeclarationStatement>()
                .Where(declaration => declaration.Initializer is null)
                .Select(declaration => declaration.Local)
                .Concat(PatternLocals(root.Root))
                .Distinct<LocalVariable>(ReferenceEqualityComparer.Instance);

            var graph = FlowGraphBuilder.Build(root.Root);
            var slots = new LocalAssignmentSlots(tracked, root.Root);
            var flow = new DefiniteAssignment(context, slots, storage, constructor, LocalFunctionReads.Of(graph));
            var solution = flow.Judge(graph, DefiniteAssignment.Nothing);

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

    private static IEnumerable<LocalVariable> PatternLocals(object body)
    {
        return BodyWalker.DescendantsAndSelf(body).OfType<Pattern>()
            .Select(pattern => pattern.Binding).OfType<LocalVariable>();
    }

    private static List<object> Unassigned(ConcreteType owner)
    {
        var storage = new List<object>();

        foreach(var field in Defaultability.InstanceFields(owner))
        {
            if(field.Initializer is null && !Defaultability.IsUndecided(field.Type) && !Defaultability.FieldHasDefault(owner, field))
            {
                storage.Add(field);
            }
        }

        foreach(var property in owner.PropertiesList)
        {
            if(property is { Modifiers.IsStatic: false, Initializer: null, Getter.IsAuto: true }
                && !Defaultability.IsUndecided(property.Type) && !Defaultability.HasDefault(property.Type))
            {
                storage.Add(property);
            }
        }

        return storage;
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
        LocalAssignmentSlots slots,
        IReadOnlyList<object> storage,
        Constructor? constructor,
        IReadOnlyDictionary<Method, IReadOnlySet<LocalStoragePath>> functionReads) : IFlowLattice<ImmutableHashSet<object>>
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
                    => state.Except(slots.Required(new LocalStoragePath(declaration.Local))),
                { Kind: FlowStepKind.Declare or FlowStepKind.Introduce or FlowStepKind.Bind, Variable: { } declared }
                    => state.Union(slots.Written(new LocalStoragePath(declared))),
                { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false } assignment }
                    => state.Union(Written(assignment.Target)),
                { Kind: FlowStepKind.Evaluate, IsStorageReceiver: false, Node: Expression expression }
                    when LocalStoragePath.Of(expression) is { } read && slots.Tracks(read)
                    => state.Union(slots.Required(read)),
                { Kind: FlowStepKind.Evaluate } when Entered(step.Node) is { } reads
                    => state.Union(reads.SelectMany(slots.Required)),
                _ => state,
            };
        }

        private IReadOnlySet<LocalStoragePath>? Entered(object node)
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
            if(step.Kind != FlowStepKind.Evaluate || step.IsStorageReceiver)
            {
                return;
            }

            switch(step.Node)
            {
                case Expression expression when LocalStoragePath.Of(expression) is { } read
                    && slots.Tracks(read) && !slots.Required(read).All(state.Contains):
                    context.Report(ProgramAnalysisDiagnostics.LocalReadBeforeAssignment, expression.Node, [expression], read.Name);
                    break;

                case MemberAccessExpression { Receiver: ThisExpression } access
                    when Storage(access.Member) is { } member && storage.Contains(member) && !state.Contains(member):
                    context.Report(
                        ProgramAnalysisDiagnostics.FieldReadBeforeAssignment, access.Node, [access],
                        NameOf(member), constructor!.Name);
                    break;

                case LambdaExpression or AsyncBlockExpression when graph.Nested.TryGetValue(step.Node, out var nested):
                    _ = new DefiniteAssignment(context, slots, [], constructor: null, functionReads).Judge(nested, state);
                    break;

                case LocalFunctionStatement function when graph.Nested.TryGetValue(step.Node, out var nested):
                    var entry = state.Union(slots.All).Except(PatternLocals(function.Body)
                        .SelectMany(variable => slots.Required(new LocalStoragePath(variable))));
                    _ = new DefiniteAssignment(context, slots, [], constructor: null, functionReads).Judge(nested, entry);
                    break;

                case InvocationExpression or FunctionReferenceExpression
                    when LocalFunctionReads.Entered(step.Node) is { } function && functionReads.TryGetValue(function, out var reads):
                    foreach(var read in reads.Where(read => slots.Tracks(read) && !slots.Required(read).All(state.Contains))
                        .GroupBy(read => read.Local).Select(group => group.First()))
                    {
                        context.Report(
                            ProgramAnalysisDiagnostics.LocalFunctionReadsUnassigned, ((Expression)step.Node).Node,
                            [(Expression)step.Node], function.Name, read.Name);
                    }

                    break;
            }
        }

        private IEnumerable<object> Written(Expression target)
        {
            if(target is SpreadExpression spread)
            {
                return Written(spread.Operand);
            }

            if(LocalStoragePath.Of(target) is { } path)
            {
                return slots.Written(path);
            }

            var member = target switch
            {
                FieldReferenceExpression field => field.Field,
                PropertyReferenceExpression property => property.Property,
                MemberAccessExpression { Receiver: ThisExpression } access => Storage(access.Member),
                _ => null,
            };

            return member is null ? [] : [member];
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