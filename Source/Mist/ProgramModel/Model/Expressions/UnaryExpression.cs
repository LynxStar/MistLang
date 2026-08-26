using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A unary operation (e.g., <c>-x</c>, <c>!flag</c>).
/// </summary>
internal sealed class UnaryExpression(
    SyntaxNode node,
    UnaryOperatorKind operatorKind,
    Expression operand,
    IMistType type) : Expression(node)
{
    public UnaryOperatorKind OperatorKind { get; } = operatorKind;

    public Expression Operand { get; } = operand;

    public override IMistType Type { get; } = type;
}

/// <summary>
/// The kind of unary operator applied.
/// </summary>
internal enum UnaryOperatorKind
{
    Negate,
    LogicalNot,
    FromEnd,
}