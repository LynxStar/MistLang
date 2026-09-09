using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A stack allocation (<c>stackalloc T[size]</c>), whose <see cref="Expression.Type"/> is the span it
/// produces. Distinct from <see cref="ArrayCreationExpression"/> despite the identical shape: the two
/// differ in where the memory lives, which is the whole of what the syntax chooses between.
/// </summary>
internal sealed class StackallocExpression(
    SyntaxNode node,
    IMistType elementType,
    Expression size,
    IMistType type) : Expression(node)
{
    public IMistType ElementType { get; } = elementType;

    public Expression Size { get; } = size;

    public override IMistType Type { get; } = type;
}