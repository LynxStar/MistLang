using Mist.Generated;
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

    /// <summary>
    /// Indicates the type has a target-language backing participating in the
    /// backend's Backing_Type_Map (i.e., a type-level <c>intrinsic</c> modifier
    /// was present on its declaration). The flag is set by the semantic model
    /// builder and read by the emitter; it is immutable after construction.
    /// </summary>
    public bool IsBackendBacked { get; }

    /// <summary>
    /// The defaultability status of this type.
    /// </summary>
    public DefaultabilityStatus Defaultability { get; internal set; } = DefaultabilityStatus.Unknown;

    /// <summary>
    /// The computed default field values for this type, keyed by field name.
    /// Null if the type is not defaultable.
    /// </summary>
    public IReadOnlyDictionary<string, DefaultFieldValue>? DefaultValues { get; internal set; }

    /// <summary>
    /// The AST node for the explicit default block, if one was declared.
    /// Null for inferred defaults and non-defaultable types.
    /// </summary>
    internal DefaultBlockDeclarationNode? DefaultBlockNode { get; set; }

    public ClassType(
        string displayName,
        IScope memberScope,
        string? containingNamespace,
        bool isBackendBacked = false)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
        BaseType = null;
        IsBackendBacked = isBackendBacked;
    }
}
