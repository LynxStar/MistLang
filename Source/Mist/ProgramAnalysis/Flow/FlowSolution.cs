namespace Mist.ProgramAnalysis.Flow;

internal interface IFlowLattice<TState>
{
    TState Join(TState left, TState right);

    bool Same(TState left, TState right);

    TState Transfer(TState state, FlowStep step);

    TState PrepareWiden(TState joined, TState incoming, FlowBlock header)
    {
        return joined;
    }

    TState Widen(TState previous, TState joined, int round)
    {
        return joined;
    }
}

/// <summary>
/// Reachability is separate from lattice state: a finite lattice can legitimately produce its bottom
/// value (all locals assigned is intersection's identity), so that value cannot also mean unreachable.
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
        var changes = new int[count];
        var headers = new bool[count];
        foreach(var loop in graph.Loops)
        {
            headers[loop.Header.Ordinal] = true;
        }

        var outputs = new TState[count];
        var available = new bool[count];
        var predecessors = Enumerable.Range(0, count).Select(_ => new List<FlowBlock>()).ToArray();
        foreach(var source in graph.Blocks)
        {
            foreach(var target in source.Successors)
            {
                predecessors[target.Ordinal].Add(source);
            }
        }

        var order = ReversePostorder(graph);
        var pending = new PriorityQueue<FlowBlock, int>();

        entries[graph.Entry.Ordinal] = entry;
        reached[graph.Entry.Ordinal] = true;
        queued[graph.Entry.Ordinal] = true;
        pending.Enqueue(graph.Entry, order[graph.Entry.Ordinal]);

        while(pending.TryDequeue(out var block, out _))
        {
            queued[block.Ordinal] = false;
            var state = entries[block.Ordinal];

            foreach(var step in block.Steps)
            {
                state = lattice.Transfer(state, step);
            }

            if(available[block.Ordinal] && lattice.Same(outputs[block.Ordinal], state))
            {
                continue;
            }

            outputs[block.Ordinal] = state;
            available[block.Ordinal] = true;
            foreach(var successor in block.Successors)
            {
                var index = successor.Ordinal;
                // Rebuild from current predecessor outputs so a widening summary replaces earlier rounds on every path through the body.
                var hasInput = ReferenceEquals(successor, graph.Entry);
                var joined = entry;
                foreach(var predecessor in predecessors[index])
                {
                    if(available[predecessor.Ordinal])
                    {
                        joined = hasInput ? lattice.Join(joined, outputs[predecessor.Ordinal]) : outputs[predecessor.Ordinal];
                        hasInput = true;
                    }
                }

                if(reached[index])
                {
                    var previous = entries[index];
                    if(lattice.Same(joined, previous))
                    {
                        continue;
                    }

                    var round = ++changes[index];
                    if(headers[index])
                    {
                        var incoming = entry;
                        var supplied = ReferenceEquals(successor, graph.Entry);
                        foreach(var predecessor in predecessors[index])
                        {
                            if(available[predecessor.Ordinal] && order[predecessor.Ordinal] < order[index])
                            {
                                incoming = supplied ? lattice.Join(incoming, outputs[predecessor.Ordinal]) : outputs[predecessor.Ordinal];
                                supplied = true;
                            }
                        }

                        joined = lattice.PrepareWiden(joined, incoming, successor);
                        joined = lattice.Widen(previous, joined, round);
                    }

                    if(lattice.Same(joined, previous))
                    {
                        continue;
                    }
                }

                entries[index] = joined;
                reached[index] = true;

                if(!queued[index])
                {
                    queued[index] = true;
                    pending.Enqueue(successor, order[index]);
                }
            }
        }

        return new FlowSolution<TState>(graph, lattice, entries, reached);
    }

    /// <summary>
    /// Each block's place in reverse postorder from the entry, so the worklist takes a block only once every predecessor that is
    /// not behind a back edge has settled. Successors are explored last first so a block's first successor -- the path a
    /// condition holds on -- comes ahead of its others.
    /// </summary>
    private static int[] ReversePostorder(FlowGraph graph)
    {
        var order = new int[graph.Blocks.Count];
        var visited = new bool[graph.Blocks.Count];
        var next = graph.Blocks.Count;
        var path = new Stack<(FlowBlock Block, int Remaining)>();
        visited[graph.Entry.Ordinal] = true;
        path.Push((graph.Entry, graph.Entry.Successors.Count));

        while(path.TryPop(out var frame))
        {
            if(frame.Remaining == 0)
            {
                order[frame.Block.Ordinal] = --next;
                continue;
            }

            path.Push((frame.Block, frame.Remaining - 1));
            var successor = frame.Block.Successors[frame.Remaining - 1];
            if(!visited[successor.Ordinal])
            {
                visited[successor.Ordinal] = true;
                path.Push((successor, successor.Successors.Count));
            }
        }

        return order;
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