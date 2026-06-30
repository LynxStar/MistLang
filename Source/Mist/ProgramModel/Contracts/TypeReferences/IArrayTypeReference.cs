namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// An array type (e.g., int[], string[]).
/// </summary>
public interface IArrayTypeReference : ITypeReference
{
    /// <summary>
    /// The element type of the array.
    /// </summary>
    ITypeReference ElementType { get; }
}