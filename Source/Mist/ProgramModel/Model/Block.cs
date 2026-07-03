using Mist.Generated;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A block of statements. Wraps the AST block node for later processing.
/// </summary>
internal sealed class Block(BlockNode node)
{
    /// <summary>
    /// The AST block node.
    /// </summary>
    public BlockNode Node { get; } = node;
}