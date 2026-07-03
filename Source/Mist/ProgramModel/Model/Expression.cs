using Mist.Generated;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An expression. Wraps the AST expression node for later processing.
/// </summary>
internal sealed class Expression(ExpressionNode node)
{
    /// <summary>
    /// The AST expression node.
    /// </summary>
    public ExpressionNode Node { get; } = node;
}