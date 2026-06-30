namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A delegate / function type (e.g., (int, int) -> string).
/// </summary>
public interface IDelegateTypeReference : ITypeReference
{
    /// <summary>
    /// The parameter types.
    /// </summary>
    IReadOnlyList<ITypeReference> ParameterTypes { get; }

    /// <summary>
    /// The return type.
    /// </summary>
    ITypeReference ReturnType { get; }
}