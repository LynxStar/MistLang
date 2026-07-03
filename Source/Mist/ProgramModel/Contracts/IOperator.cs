using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An operator declared on a type.
/// </summary>
public interface IOperator
{
    /// <summary>The operator token (e.g., "+", "-", "==").</summary>
    string OperatorToken { get; }

    /// <summary>The resolved return type.</summary>
    IMistType ReturnType { get; }

    /// <summary>The operator parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }

    /// <summary>The modifiers declared on this operator.</summary>
    MemberModifiers Modifiers { get; }
}