namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// The control flow of one body root: blocks of <see cref="FlowStep"/>s in evaluation order, joined
/// where paths meet, with one Evaluate step for a node on any path that reaches it. A lambda or async
/// block is NOT inlined -- its body is a graph of its own under <see cref="Nested"/>, keyed by the
/// expression that creates it -- because when it runs, and whether it runs at all, is not the
/// enclosing body's to know: what it assigns cannot be counted outside, and what it reads is judged
/// against the state at the point it is created.
/// </summary>
internal sealed class FlowGraph(
    FlowBlock entry,
    FlowBlock exit,
    IReadOnlyList<FlowBlock> blocks,
    IReadOnlyDictionary<object, FlowGraph> nested)
{
    public FlowBlock Entry { get; } = entry;

    public FlowBlock Exit { get; } = exit;

    public IReadOnlyList<FlowBlock> Blocks { get; } = blocks;

    public IReadOnlyDictionary<object, FlowGraph> Nested { get; } = nested;
}

internal sealed class FlowBlock(int ordinal)
{
    public int Ordinal { get; } = ordinal;

    public List<FlowStep> Steps { get; } = [];

    public List<FlowBlock> Successors { get; } = [];
}