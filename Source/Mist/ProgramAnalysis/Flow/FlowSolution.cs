namespace Mist.ProgramAnalysis.Flow;

internal interface IFlowLattice<TState>
{
    TState Join(TState left, TState right);

    bool Same(TState left, TState right);

    TState Transfer(TState state, FlowStep step);
}

/// <summary>
/// The state a forward analysis holds at the entry of every reached block once nothing changes any
/// more. Reachability is kept apart from the state rather than folded into it as a bottom element,
/// because a lattice whose universe is finite can produce the same value legitimately -- every local
/// assigned IS the identity of intersection -- and a block that no path reaches must be told from one
/// that every path reaches with nothing left to say.
/// </summary>
internal sealed class FlowSolution<TState>
{
    private readonly FlowGraph graph;
    private readonly IFlowLattice<TState> lattice;
    private readonly TState[] entries;
    private readonly bool[] reached;

    private FlowSolution(FlowGraph graph, IFlowLattice<TState> lattice, TState[] entries, bool[] reached)
    {
        this.graph = graph;
        this.lattice = lattice;
        this.entries = entries;
        this.reached = reached;
    }

    public bool ReachesExit => reached[graph.Exit.Ordinal];

    public TState AtExit => entries[graph.Exit.Ordinal];

    public static FlowSolution<TState> Solve(FlowGraph graph, IFlowLattice<TState> lattice, TState entry)
    {
        var count = graph.Blocks.Count;
        var entries = new TState[count];
        var reached = new bool[count];
        var queued = new bool[count];
        var pending = new Queue<FlowBlock>();

        entries[graph.Entry.Ordinal] = entry;
        reached[graph.Entry.Ordinal] = true;
        queued[graph.Entry.Ordinal] = true;
        pending.Enqueue(graph.Entry);

        while(pending.TryDequeue(out var block))
        {
            queued[block.Ordinal] = false;
            var state = entries[block.Ordinal];

            foreach(var step in block.Steps)
            {
                state = lattice.Transfer(state, step);
            }

            foreach(var successor in block.Successors)
            {
                var index = successor.Ordinal;

                if(reached[index])
                {
                    var joined = lattice.Join(entries[index], state);

                    if(lattice.Same(joined, entries[index]))
                    {
                        continue;
                    }

                    entries[index] = joined;
                }
                else
                {
                    entries[index] = state;
                    reached[index] = true;
                }

                if(!queued[index])
                {
                    queued[index] = true;
                    pending.Enqueue(successor);
                }
            }
        }

        return new FlowSolution<TState>(graph, lattice, entries, reached);
    }

    public bool Reaches(FlowBlock block)
    {
        return reached[block.Ordinal];
    }

    public TState At(FlowBlock block)
    {
        return entries[block.Ordinal];
    }

    public void Replay(Action<TState, FlowStep> judge)
    {
        foreach(var block in graph.Blocks)
        {
            if(!reached[block.Ordinal])
            {
                continue;
            }

            var state = entries[block.Ordinal];

            foreach(var step in block.Steps)
            {
                judge(state, step);
                state = lattice.Transfer(state, step);
            }
        }
    }
}