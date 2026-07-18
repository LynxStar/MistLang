using Mist.Generated;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A statement kind the body binder does not yet resolve. Retains the AST node so
/// a later pass can resolve it.
/// </summary>
internal sealed class UnsupportedStatement(StatementNode node) : Statement
{
    /// <summary>
    /// The unresolved AST statement node.
    /// </summary>
    public StatementNode Node { get; } = node;
}