using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An operator declared on a type.
/// </summary>
public interface IOperator
{
    /// <summary>The operator token (e.g., "+", "-", "==").</summary>
    string OperatorToken { get; }

    /// <summary>The resolved return type.</summary>
    ITypeReference ReturnType { get; }

    /// <summary>The operator parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }
}