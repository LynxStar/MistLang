using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined interface type.
/// </summary>
public sealed class InterfaceType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Interface;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }
    public IReadOnlyList<InterfaceType> Interfaces { get; internal set; } = Array.Empty<InterfaceType>();

    public InterfaceType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
    }
}
