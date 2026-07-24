using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An operator declared on a type. Operators are methods with a synthesized
/// name (e.g., op_Addition) and an operator kind instead of a user-defined name.
/// </summary>
public interface IOperator : IMethod
{
    /// <summary>The kind of operator.</summary>
    OperatorKind Kind { get; }

    /// <summary>Whether this is a unary operator (one parameter) vs binary (two parameters).</summary>
    bool IsUnary { get; }
}