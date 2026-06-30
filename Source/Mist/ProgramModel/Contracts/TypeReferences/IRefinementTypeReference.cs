namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A refinement type -- a base type with an additional constraint predicate.
/// </summary>
public interface IRefinementTypeReference : ITypeReference
{
    /// <summary>
    /// The underlying base type being refined.
    /// </summary>
    ITypeReference BaseType { get; }
}