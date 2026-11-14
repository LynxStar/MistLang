using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A method named by synthesis from its <see cref="Kind"/> (op_Addition) rather than by the user.
/// </summary>
public interface IOperator : IMethod
{
    OperatorKind Kind { get; }

    bool IsUnary { get; }
}