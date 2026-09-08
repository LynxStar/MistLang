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

    /// <summary>
    /// Written <c>readonly { ... }</c>, which scopes a promise about what the block may MUTATE
    /// rather than changing what it computes.
    /// <para>
    /// Recorded even though no C# carries it, because erasing it here would leave nothing to
    /// enforce it FROM: the rule it stands for is a judgement on a finished model, and a model that
    /// forgot the block was readonly could not make it without re-reading the source.
    /// </para>
    /// </summary>
    public bool IsReadonly { get; internal set; }
}