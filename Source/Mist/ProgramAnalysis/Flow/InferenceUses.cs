using System.Collections.Immutable;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Flow;

internal sealed class InferenceUses : IFlowLattice<ImmutableDictionary<LocalVariable, int>>
{
    private readonly Dictionary<LocalVariable, HashSet<LocalVariable>> sources;
    private readonly Dictionary<LocalVariable, Block> homes;

    private InferenceUses(object root, IReadOnlyList<LocalDeclarationStatement> candidates)
    {
        sources = candidates.ToDictionary(declaration => declaration.Local,
            declaration => declaration.Initializer is { } value ? LocalOwnership.Sources(value).Select(place => place.Root).ToHashSet() : []);
        homes = BodyWalker.DescendantsAndSelf(root).OfType<Block>()
            .SelectMany(block => block.Statements.OfType<LocalDeclarationStatement>().Select(declaration => (declaration.Local, Block: block)))
            .ToDictionary(pair => pair.Local, pair => pair.Block);
    }

    public HashSet<LocalVariable> Later { get; } = [];

    public HashSet<LocalVariable> Overlapping { get; } = [];

    public HashSet<LocalVariable> MutatingOverlap { get; } = [];

    public static InferenceUses Of(object root, IReadOnlyList<LocalDeclarationStatement> candidates, FlowGraph graph)
    {
        var uses = new InferenceUses(root, candidates);
        var mutations = new HashSet<Expression>(ReferenceEqualityComparer.Instance);
        var tiers = new ThisTiers();
        foreach(var call in BodyWalker.DescendantsAndSelf(root).OfType<InvocationExpression>())
        {
            if(call.Method is { } method && tiers.Required(method) >= ThisTier.Borrowed && ThisTiers.ReceiverOf(call) is { } receiver)
            {
                mutations.UnionWith(BodyWalker.DescendantsAndSelf(receiver).OfType<LocalReferenceExpression>());
            }

            for(var i = 0; call.Parameters is { } parameters && i < parameters.Count && i < call.Arguments.Count; i++)
            {
                if(parameters[i].Modifier.Ownership == OwnershipKind.Borrowed)
                {
                    mutations.UnionWith(BodyWalker.DescendantsAndSelf(call.Arguments[i]).OfType<LocalReferenceExpression>());
                }
            }
        }

        FlowSolution<ImmutableDictionary<LocalVariable, int>>.Solve(graph, uses, ImmutableDictionary<LocalVariable, int>.Empty)
            .Replay((state, step) =>
            {
                if(step is { Kind: FlowStepKind.Evaluate, Node: LocalReferenceExpression reference })
                {
                    foreach(var (local, active) in state)
                    {
                        if(uses.sources[local].Contains(reference.Local))
                        {
                            _ = uses.Later.Add(local);
                            if((active & 1) != 0)
                            {
                                _ = uses.Overlapping.Add(local);
                                if(mutations.Contains(reference))
                                {
                                    _ = uses.MutatingOverlap.Add(local);
                                }
                            }
                        }
                    }
                }
                else if(step.Kind == FlowStepKind.Evaluate && step.Node switch
                {
                    AssignmentExpression { Target: not LocalReferenceExpression } assignment => assignment.Target,
                    CompoundAssignmentExpression compound => compound.Target,
                    _ => null,
                } is { } target)
                {
                    var written = BodyWalker.DescendantsAndSelf(target).OfType<LocalReferenceExpression>().Select(reference => reference.Local).ToHashSet();
                    foreach(var (local, active) in state)
                    {
                        if((active & 1) != 0 && uses.sources[local].Overlaps(written))
                        {
                            _ = uses.Later.Add(local);
                            _ = uses.Overlapping.Add(local);
                            _ = uses.MutatingOverlap.Add(local);
                        }
                    }
                }
            });
        return uses;
    }

    public ImmutableDictionary<LocalVariable, int> Join(ImmutableDictionary<LocalVariable, int> left, ImmutableDictionary<LocalVariable, int> right)
    {
        foreach(var (local, state) in right)
        {
            left = left.SetItem(local, left.GetValueOrDefault(local) | state);
        }

        return left;
    }

    public bool Same(ImmutableDictionary<LocalVariable, int> left, ImmutableDictionary<LocalVariable, int> right)
    {
        return left.Count == right.Count && left.All(pair => right.GetValueOrDefault(pair.Key) == pair.Value);
    }

    public ImmutableDictionary<LocalVariable, int> Transfer(ImmutableDictionary<LocalVariable, int> state, FlowStep step)
    {
        return step switch
        {
            { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration } when sources.ContainsKey(declaration.Local)
                => state.SetItem(declaration.Local, 1),
            { Kind: FlowStepKind.Leave, Node: Block block } => state.SetItems(state.Where(pair => homes.GetValueOrDefault(pair.Key) == block).Select(pair => new KeyValuePair<LocalVariable, int>(pair.Key, 2))),
            { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { Target: LocalReferenceExpression reference } }
                => state.RemoveRange(state.Keys.Where(local => sources[local].Contains(reference.Local))),
            { Kind: FlowStepKind.Evaluate, Node: CompoundAssignmentExpression { Target: LocalReferenceExpression incremented } }
                => state.RemoveRange(state.Keys.Where(local => sources[local].Contains(incremented.Local))),
            _ => state,
        };
    }
}