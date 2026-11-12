using Mist.Generated;

namespace Mist.ProgramModel.Model.Statements;

internal sealed class Block(BlockNode node) : Statement
{
    public BlockNode Node { get; } = node;

    /// <summary>
    /// Empty until Phase 3 body binding fills it.
    /// </summary>
    public IReadOnlyList<Statement> Statements { get; internal set; } = [];

    /// <summary>
    /// Written <c>readonly { ... }</c>, a promise about what the block may MUTATE. Recorded though no C#
    /// carries it, because the rule it stands for judges the finished model, which could not otherwise
    /// tell without re-reading the source.
    /// </summary>
    public bool IsReadonly { get; internal set; }
}