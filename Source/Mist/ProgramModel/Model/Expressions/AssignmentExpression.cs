using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An assignment expression (<c>target = value</c>), or its coalescing form
/// (<c>target ??= value</c>), which assigns only when the target holds absence.
/// Ordinary assignments carry the storage result type; coalescing statements carry the present value type.
/// </summary>
internal sealed class AssignmentExpression(
    SyntaxNode node,
    Expression target,
    Expression value,
    IMistType type,
    bool isCoalescing = false) : Expression(node)
{
    /// <summary>
    /// The assignment target (variable, field, array element, etc.).
    /// </summary>
    public Expression Target { get; } = target;

    public bool IsCoalescing { get; } = isCoalescing;

    /// <summary>
    /// The value being assigned.
    /// </summary>
    public Expression Value { get; } = value;

    public override IMistType Type { get; } = type;
}