using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An operator declared on a type.
/// </summary>
public interface IOperator
{
    /// <summary>The kind of operator.</summary>
    OperatorKind Kind { get; }

    /// <summary>The resolved return type.</summary>
    IMistType ReturnType { get; }

    /// <summary>The operator parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }

    /// <summary>The modifiers declared on this operator.</summary>
    MemberModifiers Modifiers { get; }

    /// <summary>Whether this is a unary operator (one parameter) vs binary (two parameters).</summary>
    bool IsUnary { get; }
}