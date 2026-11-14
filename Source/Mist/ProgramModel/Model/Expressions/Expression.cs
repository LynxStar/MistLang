using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression within a resolved body. Its <see cref="Node"/> is kept as a convenience, never on a
/// contract; concrete kinds are matched by type.
/// </summary>
internal abstract class Expression(SyntaxNode node)
{
    public SyntaxNode Node { get; } = node;

    /// <summary>
    /// Unresolved expressions carry the error sentinel type.
    /// </summary>
    public abstract IMistType Type { get; }
}