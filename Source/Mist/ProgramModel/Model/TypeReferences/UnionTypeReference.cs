using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// A union type (e.g., int | string | void).
/// </summary>
internal sealed class UnionTypeReference(IReadOnlyList<ITypeReference> constituents) : IUnionTypeReference
{
    public IReadOnlyList<ITypeReference> Constituents { get; } = constituents;

    public string DisplayName { get; } = string.Join(" | ", constituents.Select(c => c.DisplayName));
}