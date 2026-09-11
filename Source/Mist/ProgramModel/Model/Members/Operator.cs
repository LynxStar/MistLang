using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// An operator declared on a type. Operators are methods with a synthesized
/// name and an operator kind.
/// </summary>
internal sealed class Operator(
    OperatorKind kind,
    IMistType returnType,
    List<Parameter> parameters,
    MemberModifiers modifiers)
    : Method(MapOperatorName(kind), returnType, parameters, modifiers), IOperator
{
    public OperatorKind Kind { get; } = kind;

    public bool IsUnary => ParametersList.Count == 1;

    public string Symbol => SymbolOf(Kind);

    public static string SymbolOf(OperatorKind kind)
    {
        return kind switch
        {
            OperatorKind.Plus => "+",
            OperatorKind.Minus => "-",
            OperatorKind.Star => "*",
            OperatorKind.Slash => "/",
            OperatorKind.Modulo => "%",
            OperatorKind.EqualEqual => "==",
            OperatorKind.NotEqual => "!=",
            OperatorKind.LessThan => "<",
            OperatorKind.GreaterThan => ">",
            OperatorKind.LessThanOrEqual => "<=",
            OperatorKind.GreaterThanOrEqual => ">=",
            OperatorKind.LogicalAnd => "&&",
            OperatorKind.LogicalOr => "||",
            OperatorKind.Exclamation => "!",
            _ => throw new InvalidOperationException($"ICE: unrecognized operator kind '{kind}'"),
        };
    }

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