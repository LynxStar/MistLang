using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined trait type (structural typing).
/// </summary>
public sealed class TraitType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Trait;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }

    public TraitType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
    }
}
