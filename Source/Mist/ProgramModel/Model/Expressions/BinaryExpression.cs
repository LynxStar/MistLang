using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A binary operation (e.g., <c>a + b</c>, <c>x == y</c>).
/// The <see cref="Type"/> is the result type of the operation.
/// </summary>
internal sealed class BinaryExpression(
    SyntaxNode node,
    Expression left,
    BinaryOperatorKind operatorKind,
    Expression right,
    IMistType type) : Expression(node)
{
    public Expression Left { get; } = left;

    public BinaryOperatorKind OperatorKind { get; } = operatorKind;

    public Expression Right { get; } = right;

    public Operator? Operator { get; init; }

    public override IMistType Type { get; } = type;
}

/// <summary>
/// The kind of binary operator applied.
/// </summary>
internal enum BinaryOperatorKind
{
    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,

    // Comparison
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,

    // Logical
    LogicalAnd,
    LogicalOr,

    // String
    Concatenate,
}