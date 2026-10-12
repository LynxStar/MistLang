using System.Collections.Immutable;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// The frame storage each local in scope refers to, in <see cref="LocalFrame"/>: a ref struct local's
/// value and a ref local's referent, both fixed where the local comes into being and kept for as long
/// as it lives -- read from its initializer, the collection a foreach walks, the value a with or a
/// deconstruction is given, or the scrutinee a pattern binds from. A join keeps the narrower fact, so a
/// local that one path ties to the frame is tied on every path that follows.
/// </summary>
internal sealed class FrameLifetimes(IReadOnlyDictionary<Pattern, Expression> scrutinees)
    : IFlowLattice<ImmutableDictionary<LocalVariable, LocalFrame>>
{
    public static readonly ImmutableDictionary<LocalVariable, LocalFrame> Nothing =
        ImmutableDictionary.Create<LocalVariable, LocalFrame>(ReferenceEqualityComparer.Instance);

    public static IReadOnlyDictionary<Pattern, Expression> ScrutineesIn(IEnumerable<object> nodes)
    {
        var scrutinees = new Dictionary<Pattern, Expression>(ReferenceEqualityComparer.Instance);

        foreach(var node in nodes)
        {
            var tested = node switch
            {
                IsPatternExpression test => [(test.Pattern, test.Scrutinee)],
                SwitchExpression switched => switched.Arms.Select(arm => (arm.Pattern, switched.Scrutinee)),
                _ => [],
            };

            foreach(var (pattern, scrutinee) in tested)
            {
                foreach(var inner in BodyWalker.DescendantsAndSelf(pattern).OfType<Pattern>())
                {
                    scrutinees[inner] = scrutinee;
                }
            }
        }

        return scrutinees;
    }

    public ImmutableDictionary<LocalVariable, LocalFrame> Join(
        ImmutableDictionary<LocalVariable, LocalFrame> left, ImmutableDictionary<LocalVariable, LocalFrame> right)
    {
        var joined = left;

        foreach(var (local, frame) in right)
        {
            joined = joined.SetItem(local, joined.TryGetValue(local, out var other)
                ? new LocalFrame(other.Value ?? frame.Value, other.Referent ?? frame.Referent)
                : frame);
        }

        return joined;
    }

    public bool Same(ImmutableDictionary<LocalVariable, LocalFrame> left, ImmutableDictionary<LocalVariable, LocalFrame> right)
    {
        return left.Count == right.Count && left.All(entry => right.TryGetValue(entry.Key, out var other) && other == entry.Value);
    }

    public ImmutableDictionary<LocalVariable, LocalFrame> Transfer(ImmutableDictionary<LocalVariable, LocalFrame> state, FlowStep step)
    {
        var source = step switch
        {
            { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Initializer: var initializer } } => initializer,
            { Kind: FlowStepKind.Introduce, Node: ForEachStatement loop } => loop.Collection,
            { Kind: FlowStepKind.Introduce, Node: TupleDeconstructionStatement deconstruction } => deconstruction.Initializer,
            { Kind: FlowStepKind.Introduce, Node: WithBlockStatement with } => with.Value,
            { Kind: FlowStepKind.Bind, Node: Pattern pattern } => scrutinees.GetValueOrDefault(pattern),
            _ => null,
        };

        if(step.Variable is not { } local || step.Kind is not (FlowStepKind.Declare or FlowStepKind.Introduce or FlowStepKind.Bind))
        {
            return state;
        }

        var frame = source is null
            ? default
            : new LocalFrame(
                local.IsRef || RefStructs.Is(local.Type) ? FrameScopes.Value(source, state) : null,
                local.IsRef && step.Kind == FlowStepKind.Declare ? FrameScopes.Referent(source, state) : null);

        return frame == default ? state.Remove(local) : state.SetItem(local, frame);
    }
}

internal readonly record struct LocalFrame(string? Value, string? Referent);