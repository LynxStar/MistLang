namespace Mist.ProgramModel.Model;

/// <summary>
/// A block of statements. Wraps the AST block node for later processing.
/// </summary>
internal sealed class Block(Storm.Runtime.SyntaxNode node)
{
    /// <summary>
    /// The AST block node.
    /// </summary>
    public Storm.Runtime.SyntaxNode Node { get; } = node;
}