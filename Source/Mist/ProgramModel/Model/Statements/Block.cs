using Mist.Generated;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A block of statements forming a body or a nested scope. Holds the resolved
/// statements (filled in during Phase 3 body binding) and keeps the AST node as a
/// convenience. A block is itself a statement, so nested blocks appear in an
/// enclosing block's statement list.
/// </summary>
internal sealed class Block(BlockNode node) : Statement
{
    /// <summary>
    /// The AST block node.
    /// </summary>
    public BlockNode Node { get; } = node;

    /// <summary>
    /// The resolved statements in this block, in source order. Empty until the
    /// body binder populates them.
    /// </summary>
    public IReadOnlyList<Statement> Statements { get; internal set; } = [];
}