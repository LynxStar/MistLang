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
    /// The resolved accessibility of this struct type.
    /// </summary>
    public Accessibility Accessibility { get; internal set; }

    /// <summary>
    /// Indicates whether this struct is sealed (cannot be inherited from).
    /// Set for user-declared sealed structs.
    /// </summary>
    public bool IsSealed { get; internal set; }

    /// <summary>
    /// The base struct type, if this struct extends another struct.
    /// Null for structs with no base struct.
    /// </summary>
    public IType? BaseType { get; internal set; }

    public IReadOnlyList<IType> Abstracts { get; internal set; } = Array.Empty<IType>();
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

    /// <summary>
    /// The spread-generated member families (Requirement 10) declared on this type -- one
    /// per <c>public TArgs... Stem;</c> or <c>public TArgs... Stem { get; }</c> member
    /// spread. Each records the aggregate member and the pack whose bound length yields the
    /// element projections <c>Stem1 .. StemN</c>. Empty when the type declares no member
    /// spreads.
    /// </summary>
    public IList<SemanticSpreadMember> SpreadMembers { get; } = new List<SemanticSpreadMember>();

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
    /// Indicates whether this type is declared as readonly.
    /// When true, all members are implicitly readonly.
    /// </summary>
    public bool IsReadonly { get; internal set; }

    /// <summary>
    /// Whether this type is declared as an owned type (owned struct).
    /// In v2 this signals that the type opts into the ownership cooperation
    /// contract: the type author guarantees that every field assignment inside
    /// the type's methods and constructors comes from an owned source (a move,
    /// constructor call, owned-returning call, or implicit clone). It is a
    /// behavioral contract verified at the type definition site — not a
    /// structural requirement that the type's fields must themselves be owned.
    /// </summary>
    public bool IsOwnedType { get; internal set; }

    /// <summary>
    /// Whether this type implements IClonable&lt;T&gt; — either declared explicitly or
    /// auto-derived because all of its fields' types implement IClonable (Req 8 AC 6-7).
    /// Computed by the IClonable derivation pass; consumed by MoveBoundaryResolver.
    /// </summary>
    public bool ImplementsIClonable { get; internal set; }

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
    /// Indicates that this type's droppability originates from implementing
    /// System.IDisposable (CLR interop) rather than an explicit Mist Drop() method.
    /// When true, the emitter emits .Dispose() instead of .Drop() at drop emission sites.
    /// </summary>
    public bool IsDisposableDrop { get; internal set; }

    /// <summary>
    /// For types whose <c>Drop()</c> was SYNTHESIZED by <c>SyntheticDropInferenceEngine</c>
    /// (drop-semantics Stage 4d), the ordered list of droppable field symbols whose
    /// <c>Drop()</c> the synthesized method must call, in declaration order (Req 10.2).
    /// Null when the type has an explicit <c>Drop()</c> or no synthetic drop was generated.
    /// Read by the C# emitter (Epic 7) to emit the synthesized field-drop body.
    /// </summary>
    public IReadOnlyList<ISymbol>? SyntheticDropFields { get; internal set; }

    /// <summary>
    /// Indicates whether this type is a ref struct (stack-only, cannot be boxed).
    /// When true, the type cannot be used as a field in non-ref-struct types,
    /// as a generic type argument, in union types, or as an optional type.
    /// </summary>
    public bool IsRefStruct { get; internal set; }

    /// <summary>
    /// Indicates whether this type is stampable — a pure struct whose fields are
    /// transitively all primitives or other stampable structs. Stampable types can
    /// be used with bare <c>new T[size]</c> array creation (Stamp_Fill).
    /// Set by <c>ComputeStampability</c> during model construction.
    /// </summary>
    public bool IsStampable { get; internal set; }

    /// <summary>
    /// Returns all ancestor struct types in order from immediate parent to root.
    /// </summary>
    public IReadOnlyList<StructType> GetAncestorChain()
    {
        var ancestors = new List<StructType>();
        var current = BaseType as StructType;
        while(current is not null)
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