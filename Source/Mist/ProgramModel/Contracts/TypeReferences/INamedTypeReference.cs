namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A reference to a named type (class, struct, abstract, enum, newtype, type alias).
/// </summary>
public interface INamedTypeReference : ITypeReference
{
    /// <summary>
    /// The type definition this reference resolves to.
    /// </summary>
    IMistType Definition { get; }
}