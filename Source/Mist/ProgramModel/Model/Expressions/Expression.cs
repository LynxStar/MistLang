using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression within a resolved body. Carries its resolved <see cref="Type"/>
/// and the originating AST node (kept as a convenience, never on a contract). Root
/// of the expression hierarchy; concrete kinds are matched by type.
/// </summary>
internal abstract class Expression(SyntaxNode node)
{
    /// <summary>
    /// The AST node this expression was resolved from.
    /// </summary>
    public SyntaxNode Node { get; } = node;

    /// <summary>
    /// The resolved type of this expression. Never null; unresolved expressions
    /// carry the error sentinel type.
    /// </summary>
    public abstract IMistType Type { get; }
}