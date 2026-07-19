using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An array creation expression (<c>new T[size]</c>).
/// </summary>
internal sealed class ArrayCreationExpression(
    SyntaxNode node,
    IMistType elementType,
    Expression size,
    IMistType type) : Expression(node)
{
    public IMistType ElementType { get; } = elementType;

    public Expression Size { get; } = size;

    public override IMistType Type { get; } = type;
}