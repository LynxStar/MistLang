using System.Collections.Immutable;
using Mist.ProgramAnalysis.Flow;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FlowSolutionTests
{
    [Fact]
    public void BlocksAreVisitedInReversePostorderAndReachTheSameSolution()
    {
        var (graph, blocks) = DiamondThenLoop();
        var lattice = new Labels();

        var solution = FlowSolution<ImmutableSortedSet<string>>.Solve(graph, lattice, ImmutableSortedSet.Create<string>(StringComparer.Ordinal));

        string[] inLoop = ["a", "b1", "b2", "b3", "body", "c", "d", "entry", "head"];
        Assert.Empty(entryOf("entry"));
        Assert.Equal(["entry"], entryOf("a"));
        Assert.Equal(["a", "entry"], entryOf("b1"));
        Assert.Equal(["a", "b1", "entry"], entryOf("b2"));
        Assert.Equal(["a", "b1", "b2", "entry"], entryOf("b3"));
        Assert.Equal(["a", "entry"], entryOf("c"));
        Assert.Equal(["a", "b1", "b2", "b3", "c", "entry"], entryOf("d"));
        Assert.Equal(inLoop, entryOf("head"));
        Assert.Equal(inLoop, entryOf("body"));
        Assert.Equal(inLoop, entryOf("exit"));
        Assert.True(solution.ReachesExit);
        Assert.Equal(["entry", "a", "b1", "b2", "b3", "c", "d", "head", "body", "head", "body", "exit"], lattice.Visits);

        string[] entryOf(string label)
        {
            return [.. solution.At(blocks[label])];
        }
    }

    [Fact]
    public void OnlyAChangedLoopHeaderIsWidenedAndTheBodyRunsFromItsSummary()
    {
        var entry = new FlowBlock(0);
        var header = new FlowBlock(1);
        var body = new FlowBlock(2);
        var exit = new FlowBlock(3);
        entry.Successors.Add(header);
        header.Successors.Add(body);
        header.Successors.Add(exit);
        body.Successors.Add(header);
        body.Steps.Add(new FlowStep(FlowStepKind.Evaluate, "body"));
        var graph = new FlowGraph(entry, exit, [entry, header, body, exit],
            [new FlowLoop(header, body, null!)], new Dictionary<object, FlowGraph>());
        var lattice = new Counting();

        var solution = FlowSolution<int>.Solve(graph, lattice, 0);

        Assert.Equal([1, 2, 3], lattice.Rounds);
        Assert.Equal([(0, 1), (1, 2), (2, 3)], lattice.Inputs);
        Assert.Equal([0, 1, 2, 10], lattice.BodyEntries);
        Assert.Equal(10, solution.At(header));
        Assert.Equal(10, solution.AtExit);
    }

    private sealed class Counting : IFlowLattice<int>
    {
        public List<int> Rounds { get; } = [];
        public List<(int Previous, int Joined)> Inputs { get; } = [];
        public List<int> BodyEntries { get; } = [];

        public int Join(int left, int right)
        {
            return Math.Max(left, right);
        }

        public bool Same(int left, int right)
        {
            return left == right;
        }

        public int Transfer(int state, FlowStep step)
        {
            BodyEntries.Add(state);
            return Math.Min(state + 1, 10);
        }

        public int Widen(int previous, int joined, int round)
        {
            Rounds.Add(round);
            Inputs.Add((previous, joined));
            return round >= 3 ? 10 : joined;
        }
    }

    private static (FlowGraph Graph, Dictionary<string, FlowBlock> Blocks) DiamondThenLoop()
    {
        string[] created = ["exit", "entry", "d", "head", "a", "c", "b3", "b2", "b1", "body"];
        var blocks = created.Select((label, ordinal) => (label, block: new FlowBlock(ordinal))).ToDictionary(pair => pair.label, pair => pair.block);
        foreach(var (label, block) in blocks)
        {
            block.Steps.Add(new FlowStep(FlowStepKind.Evaluate, label));
        }

        (string From, string To)[] edges =
        [
            ("entry", "a"), ("a", "b1"), ("a", "c"), ("b1", "b2"), ("b2", "b3"), ("b3", "d"), ("c", "d"),
            ("d", "head"), ("head", "body"), ("head", "exit"), ("body", "head"),
        ];
        foreach(var (from, to) in edges)
        {
            blocks[from].Successors.Add(blocks[to]);
        }

        var graph = new FlowGraph(blocks["entry"], blocks["exit"], [.. created.Select(label => blocks[label])], [], new Dictionary<object, FlowGraph>());
        return (graph, blocks);
    }

    private sealed class Labels : IFlowLattice<ImmutableSortedSet<string>>
    {
        public List<string> Visits { get; } = [];

        public ImmutableSortedSet<string> Join(ImmutableSortedSet<string> left, ImmutableSortedSet<string> right)
        {
            return left.Union(right);
        }

        public bool Same(ImmutableSortedSet<string> left, ImmutableSortedSet<string> right)
        {
            return left.SetEquals(right);
        }

        public ImmutableSortedSet<string> Transfer(ImmutableSortedSet<string> state, FlowStep step)
        {
            var label = (string)step.Node;
            Visits.Add(label);
            return state.Add(label);
        }
    }
}