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

    /// <summary>Type parameters declared on this trait (empty for non-generic traits).</summary>
    public IReadOnlyList<TypeParameterType> TypeParameters { get; internal set; } = Array.Empty<TypeParameterType>();

    /// <summary>
    /// Resolved parent traits (may be <see cref="TraitType"/> or <see cref="ConstructedTraitType"/>).
    /// Empty for traits with no inheritance clause.
    /// </summary>
    public IReadOnlyList<IType> ParentTraits { get; internal set; } = Array.Empty<IType>();

    /// <summary>
    /// Indicates whether this trait is involved in a circular inheritance chain.
    /// Cyclic traits are excluded from satisfaction computation.
    /// </summary>
    public bool IsCyclic { get; internal set; }

    /// <summary>
    /// Indicates whether this trait is declared as readonly.
    /// When true, all members are implicitly readonly.
    /// </summary>
    public bool IsReadonly { get; internal set; }

    public TraitType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
    }
}
