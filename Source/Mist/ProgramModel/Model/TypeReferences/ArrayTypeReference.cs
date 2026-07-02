using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// An array type (e.g., int[], string[]).
/// </summary>
internal sealed class ArrayTypeReference(ITypeReference elementType) : IArrayTypeReference
{
    public ITypeReference ElementType { get; } = elementType;

    public string DisplayName { get; } = $"{elementType.DisplayName}[]";
}