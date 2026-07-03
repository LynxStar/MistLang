namespace Mist.ProgramModel.Model;

/// <summary>
/// An expression. Wraps the AST expression node for later processing.
/// </summary>
internal sealed class Expression(Storm.Runtime.SyntaxNode node)
{
    /// <summary>
    /// The AST expression node.
    /// </summary>
    public Storm.Runtime.SyntaxNode Node { get; } = node;
}