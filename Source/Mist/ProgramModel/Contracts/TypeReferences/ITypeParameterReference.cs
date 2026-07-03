namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A reference to a type parameter (e.g., T in a generic context).
/// </summary>
public interface ITypeParameterReference : ITypeReference
{
    /// <summary>
    /// The type parameter definition this reference refers to.
    /// </summary>
    ITypeParameter Parameter { get; }
}