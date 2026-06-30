namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A union type (e.g., int | string | void).
/// </summary>
public interface IUnionTypeReference : ITypeReference
{
    /// <summary>
    /// The constituent types of the union.
    /// </summary>
    IReadOnlyList<ITypeReference> Constituents { get; }
}