using Mist.Generated;
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
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

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

    /// <summary>
    /// Traits that this type is known to satisfy (populated by with-blocks and call-site checking).
    /// </summary>
    public List<IType> SatisfiedTraits { get; } = new();

    /// <summary>
    /// Indicates whether this type is declared as readonly.
    /// When true, all members are implicitly readonly.
    /// </summary>
    public bool IsReadonly { get; internal set; }

    public StructType(
        string displayName,
        IScope memberScope,
        string? containingNamespace,
        bool isBackendBacked = false)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
        IsBackendBacked = isBackendBacked;
    }
}
