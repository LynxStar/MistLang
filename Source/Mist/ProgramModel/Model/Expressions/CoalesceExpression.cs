using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A coalescing expression (<c>left ?? right</c>). The <see cref="Type"/> is the UN-lifted
/// left type: the non-void portion of the left operand's union, since the right operand
/// supplies a value exactly when the left is absent.
/// </summary>
internal sealed class CoalesceExpression(
    SyntaxNode node,
    Expression left,
    Expression right,
    IMistType type) : Expression(node)
{
    public Expression Left { get; } = left;

    public Expression Right { get; } = right;

    public override IMistType Type { get; } = type;
}