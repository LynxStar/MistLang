using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An operator declared on a type. Operators are methods with a synthesized
/// name and an operator kind.
/// </summary>
internal sealed class Operator(
    OperatorKind kind,
    IMistType returnType,
    IReadOnlyList<IParameter> parameters,
    MemberModifiers modifiers)
    : Method(MapOperatorName(kind), returnType, parameters, modifiers), IOperator
{
    public OperatorKind Kind { get; } = kind;

    public bool IsUnary => Parameters.Count == 1;

    private static string MapOperatorName(OperatorKind kind)
    {
        return kind switch
        {
            OperatorKind.Plus => "op_Addition",
            OperatorKind.Minus => "op_Subtraction",
            OperatorKind.Star => "op_Multiply",
            OperatorKind.Slash => "op_Division",
            OperatorKind.Modulo => "op_Modulus",
            OperatorKind.EqualEqual => "op_Equality",
            OperatorKind.NotEqual => "op_Inequality",
            OperatorKind.LessThan => "op_LessThan",
            OperatorKind.GreaterThan => "op_GreaterThan",
            OperatorKind.LessThanOrEqual => "op_LessThanOrEqual",
            OperatorKind.GreaterThanOrEqual => "op_GreaterThanOrEqual",
            OperatorKind.LogicalAnd => "op_LogicalAnd",
            OperatorKind.LogicalOr => "op_LogicalOr",
            OperatorKind.Exclamation => "op_LogicalNot",
            _ => "op_Unknown",
        };
    }
}