using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An array literal expression (<c>[a, b, c]</c> or <c>[]</c>).
/// <para>
/// Distinct from <see cref="ArrayCreationExpression"/>: the length is implied by the
/// element count rather than written as a size expression, and the element type is
/// inferred rather than declared.
/// </para>
/// </summary>
internal sealed class ArrayLiteralExpression(
    SyntaxNode node,
    IMistType elementType,
    IReadOnlyList<Expression> elements,
    IMistType type) : Expression(node)
{
    public IMistType ElementType { get; } = elementType;

    /// <summary>
    /// The literal's elements in source order. Empty for the <c>[]</c> form.
    /// </summary>
    public IReadOnlyList<Expression> Elements { get; } = elements;

    public bool IsTargetTyped { get; init; }

    public override IMistType Type { get; } = type;
}