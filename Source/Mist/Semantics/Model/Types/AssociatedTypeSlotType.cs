using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents an abstract type slot declared in an interface.
/// Implementors must provide a concrete type member satisfying the slot's constraints.
/// </summary>
public sealed class AssociatedTypeSlotType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.AssociatedTypeSlot;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }

    /// <summary>The interface that declares this slot.</summary>
    public IType DeclaringType { get; }

    /// <summary>Constraints that the concrete type must satisfy.</summary>
    public TypeParameterConstraints Constraints { get; internal set; }

    /// <summary>Type parameters declared on the slot itself (for generic slots).</summary>
    public IReadOnlyList<IType> TypeParameters { get; internal set; }

    public AssociatedTypeSlotType(
        string displayName,
        IScope memberScope,
        string? containingNamespace,
        IType declaringType,
        TypeParameterConstraints constraints,
        IReadOnlyList<IType> typeParameters)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
        DeclaringType = declaringType;
        Constraints = constraints;
        TypeParameters = typeParameters;
    }
}
