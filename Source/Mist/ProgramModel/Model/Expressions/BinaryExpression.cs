using Mist.ProgramModel.Construction.Binding;
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

    /// <summary>
    /// What <see cref="Operator"/>'s owner and the bases walked to reach it were constructed with here. The
    /// declaration stays the one source body every closing shares, so effect judgments follow that body, while a
    /// consumer needing this site's concrete types reads them through this rather than closing them again.
    /// </summary>
    public TypeSubstitution OperatorClosing { get; init; } = TypeSubstitution.Empty;

    public IReadOnlyList<IMistType>? ParameterTypes { get; init; }

    public IReadOnlyList<ClosedOperator> AmbiguousOperators { get; init; } = [];

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