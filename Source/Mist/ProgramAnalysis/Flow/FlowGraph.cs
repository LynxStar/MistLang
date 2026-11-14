using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// Each reached node has one Evaluate step per path, in evaluation order. Lambda and async bodies
/// have separate graphs in <see cref="Nested"/>, keyed by their creating expressions: their execution
/// is unknown to the enclosing body, so their assignments do not count outside and their reads use
/// the state at creation.
/// </summary>
internal sealed class FlowGraph(
    FlowBlock entry,
    FlowBlock exit,
    IReadOnlyList<FlowBlock> blocks,
    IReadOnlyList<FlowLoop> loops,
    IReadOnlyDictionary<object, FlowGraph> nested)
{
    public FlowBlock Entry { get; } = entry;

    public FlowBlock Exit { get; } = exit;

    public IReadOnlyList<FlowBlock> Blocks { get; } = blocks;

    public IReadOnlyList<FlowLoop> Loops { get; } = loops;

    public IReadOnlyDictionary<object, FlowGraph> Nested { get; } = nested;
}

internal sealed record FlowLoop(FlowBlock Header, FlowBlock Latch, Block Body);

internal sealed class FlowBlock(int ordinal)
{
    public int Ordinal { get; } = ordinal;

    public List<FlowStep> Steps { get; } = [];

    public List<FlowBlock> Successors { get; } = [];
}