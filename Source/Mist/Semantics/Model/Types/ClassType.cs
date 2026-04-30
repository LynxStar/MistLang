using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined class type.
/// </summary>
public sealed class ClassType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Class;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }
    public IType? BaseType { get; internal set; }
    public IReadOnlyList<IType> Interfaces { get; internal set; } = Array.Empty<IType>();
    public IReadOnlyList<TypeParameterType> TypeParameters { get; internal set; } = Array.Empty<TypeParameterType>();

    public ClassType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
        BaseType = null;
    }
}
