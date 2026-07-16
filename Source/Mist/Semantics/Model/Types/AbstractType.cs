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

    /// <summary>
    /// The resolved accessibility of this abstract type.
    /// </summary>
    public Accessibility Accessibility { get; internal set; }

    public IReadOnlyList<IType> Abstracts { get; internal set; } = Array.Empty<IType>();
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

    /// <summary>
    /// Methods with default implementations. Key = method name.
    /// These do NOT need to be filled by implementers.
    /// </summary>
    public Dictionary<string, ISymbol> DefaultMethods { get; } = new();

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