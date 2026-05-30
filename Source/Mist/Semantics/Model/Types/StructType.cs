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

    /// <summary>
    /// The base struct type, if this struct extends another struct.
    /// Null for structs with no base struct.
    /// </summary>
    public IType? BaseType { get; internal set; }

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

    /// <summary>
    /// Whether this type is declared as an owned type (owned struct).
    /// When true, instances must always be held as owned and all reference-type
    /// fields must themselves be owned.
    /// </summary>
    public bool IsOwnedType { get; internal set; }

    /// <summary>
    /// Indicates whether this type is a ref struct (stack-only, cannot be boxed).
    /// When true, the type cannot be used as a field in non-ref-struct types,
    /// as a generic type argument, in union types, or as an optional type.
    /// </summary>
    public bool IsRefStruct { get; internal set; }

    /// <summary>
    /// Returns all ancestor struct types in order from immediate parent to root.
    /// </summary>
    public IReadOnlyList<StructType> GetAncestorChain()
    {
        var ancestors = new List<StructType>();
        var current = BaseType as StructType;
        while (current is not null)
        {
            ancestors.Add(current);
            current = current.BaseType as StructType;
        }
        return ancestors;
    }

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
