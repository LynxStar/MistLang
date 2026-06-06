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
    /// Whether this type is declared as an owned type (owned class).
    /// When true, instances must always be held as owned and all reference-type
    /// fields must themselves be owned.
    /// </summary>
    public bool IsOwnedType { get; internal set; }

    /// <summary>
    /// The substructural discipline declared on this type.
    /// Drives ownership and Drop() enforcement for affine/linear/relevant types.
    /// </summary>
    public SubstructuralDiscipline Discipline { get; internal set; } = SubstructuralDiscipline.None;

    /// <summary>
    /// Whether this type has a Drop() method (explicit or synthesized).
    /// </summary>
    public bool HasDrop { get; internal set; }

    /// <summary>
    /// The return type of the Drop() method, or null if Drop() returns void or is absent.
    /// </summary>
    public IType? DropReturnType { get; internal set; }

    /// <summary>
    /// For types whose <c>Drop()</c> was SYNTHESIZED by <c>SyntheticDropInferenceEngine</c>
    /// (drop-semantics Stage 4d), the ordered list of droppable field symbols whose
    /// <c>Drop()</c> the synthesized method must call, in declaration order (Req 10.2).
    /// Null when the type has an explicit <c>Drop()</c> or no synthetic drop was generated.
    /// Read by the C# emitter (Epic 7) to emit the synthesized field-drop body.
    /// </summary>
    public IReadOnlyList<ISymbol>? SyntheticDropFields { get; internal set; }

    /// <summary>
    /// Indicates whether this CLR class is abstract (cannot be instantiated directly).
    /// Only set for types projected from CLR assemblies.
    /// </summary>
    public bool IsAbstract { get; internal set; }

    /// <summary>
    /// Indicates whether this CLR class is sealed (cannot be inherited from).
    /// Only set for types projected from CLR assemblies.
    /// </summary>
    public bool IsSealed { get; internal set; }

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
