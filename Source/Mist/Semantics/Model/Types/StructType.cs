using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined struct type.
/// </summary>
public sealed class StructType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Struct;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }
    public IReadOnlyList<IType> Interfaces { get; internal set; } = Array.Empty<IType>();
    public IReadOnlyList<TypeParameterType> TypeParameters { get; internal set; } = Array.Empty<TypeParameterType>();

    public StructType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
    }
}
