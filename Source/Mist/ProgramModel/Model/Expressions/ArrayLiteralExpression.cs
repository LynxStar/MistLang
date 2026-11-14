using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// Distinct from <see cref="ArrayCreationExpression"/>: the length is implied by the element count
/// rather than written as a size expression, and the element type is inferred rather than declared.
/// </summary>
internal sealed class ArrayLiteralExpression(
    SyntaxNode node,
    IMistType elementType,
    IReadOnlyList<Expression> elements,
    IMistType type) : Expression(node)
{
    public IMistType ElementType { get; } = elementType;

    public IReadOnlyList<Expression> Elements { get; } = elements;

    public bool IsTargetTyped { get; init; }

    public override IMistType Type { get; } = type;
}