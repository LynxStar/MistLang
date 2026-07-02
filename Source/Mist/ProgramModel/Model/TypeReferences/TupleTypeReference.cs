using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// A tuple type (e.g., (int, string)).
/// </summary>
internal sealed class TupleTypeReference(IReadOnlyList<ITypeReference> elementTypes) : ITupleTypeReference
{
    public IReadOnlyList<ITypeReference> ElementTypes { get; } = elementTypes;

    public string DisplayName { get; } = $"({string.Join(", ", elementTypes.Select(e => e.DisplayName))})";
}