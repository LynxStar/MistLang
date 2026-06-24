using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined abstract type.
/// </summary>
public sealed class AbstractType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Abstract;
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }
    public IReadOnlyList<IType> Abstracts { get; internal set; } = Array.Empty<IType>();
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

    /// <summary>
    /// Indicates whether this abstract type is declared as readonly.
    /// When true, all members are implicitly readonly.
    /// </summary>
    public bool IsReadonly { get; internal set; }

    public AbstractType(string displayName, IScope memberScope, string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
    }
}
