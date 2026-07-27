#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the program model construction phase.
/// Covers namespace validation, type registration, type resolution,
/// base type resolution, and member resolution.
/// </summary>
public static class ProgramModelDiagnostics
{
    // ── Internal / driver-level errors ────────────────────────────────────

    /// <summary>AST array length does not match compilation unit array length.</summary>
    public static readonly DiagnosticDescriptor AstUnitMismatch = new(
        new DiagnosticId("MIST_INTERNAL_AST_UNIT_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: AST count ({0}) does not match compilation unit count ({1}). This indicates a driver bug where a failed lex/parse dropped an AST without dropping its corresponding compilation unit.");

    /// <summary>An unexpected namespace declaration node type was encountered.</summary>
    public static readonly DiagnosticDescriptor UnexpectedNamespaceNode = new(
        new DiagnosticId("MIST_INTERNAL_UNEXPECTED_NS_NODE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unexpected namespace declaration node type '{0}'.");

    /// <summary>An unrecognized type declaration node was encountered during type registration.</summary>
    public static readonly DiagnosticDescriptor UnknownTypeDeclaration = new(
        new DiagnosticId("MIST_INTERNAL_UNKNOWN_TYPE_DECL"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized type declaration node '{0}'.");

    // ── Namespace validation ─────────────────────────────────────────────

    /// <summary>More than one file-scoped namespace in the same file.</summary>
    public static readonly DiagnosticDescriptor DuplicateFileScopedNamespace = new(
        new DiagnosticId("MIST0301"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Only one file-scoped namespace is permitted per file.");

    /// <summary>File-scoped and block namespaces mixed in the same file.</summary>
    public static readonly DiagnosticDescriptor MixedNamespaceStyles = new(
        new DiagnosticId("MIST0302"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "File-scoped and block namespaces cannot be mixed in the same file.");

    /// <summary>File-scoped namespace inside a block namespace.</summary>
    public static readonly DiagnosticDescriptor FileScopedInsideBlock = new(
        new DiagnosticId("MIST0305"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "File-scoped namespace declarations cannot appear inside a block namespace.");

    // ── Type resolution ──────────────────────────────────────────────────

    /// <summary>A type with the same name already exists in this namespace.</summary>
    public static readonly DiagnosticDescriptor DuplicateTypeName = new(
        new DiagnosticId("MIST_PM_DUPLICATE_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A type named '{0}' already exists in namespace '{1}'.");

    /// <summary>A type reference could not be resolved in any reachable scope.</summary>
    public static readonly DiagnosticDescriptor UnresolvedType = new(
        new DiagnosticId("MIST_PM_UNRESOLVED_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type '{0}' could not be found.");

    /// <summary>A type name resolves to multiple distinct types from different imported namespaces at the same scope level.</summary>
    public static readonly DiagnosticDescriptor AmbiguousType = new(
        new DiagnosticId("MIST_PM_AMBIGUOUS_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type name '{0}' is ambiguous between imported namespaces '{1}' and '{2}'. Use a fully-qualified name to disambiguate.");

    /// <summary>An unrecognized type reference AST node was encountered.</summary>
    public static readonly DiagnosticDescriptor UnknownTypeRefNode = new(
        new DiagnosticId("MIST_PM_UNKNOWN_TYPE_REF_NODE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized type reference node '{0}'.");

    /// <summary>A tuple element is named with a reserved positional name (e.g., ItemN).</summary>
    public static readonly DiagnosticDescriptor ReservedTupleElementName = new(
        new DiagnosticId("MIST_PM_RESERVED_TUPLE_ELEMENT_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is a reserved tuple element name; it collides with the positional element accessors. Choose a different name.");

    // ── Base type resolution ─────────────────────────────────────────────

    /// <summary>A concrete type attempts to inherit from an incompatible kind.</summary>
    public static readonly DiagnosticDescriptor BaseTypeKindMismatch = new(
        new DiagnosticId("MIST_PM_BASE_TYPE_KIND_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A {0} cannot inherit from a {1} ('{2}').");

    /// <summary>A concrete type appears in the abstract implementations list.</summary>
    public static readonly DiagnosticDescriptor ConcreteInAbstractList = new(
        new DiagnosticId("MIST_PM_CONCRETE_IN_ABSTRACT_LIST"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A concrete type ('{0}') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.");

    /// <summary>A non-abstract type is used where only an abstract is permitted.</summary>
    public static readonly DiagnosticDescriptor InvalidBaseType = new(
        new DiagnosticId("MIST_PM_INVALID_BASE_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not an abstract and cannot be used as a base type.");

    // ── Member resolution ────────────────────────────────────────────────

    /// <summary>An unrecognized abstract member declaration node.</summary>
    public static readonly DiagnosticDescriptor UnknownAbstractMember = new(
        new DiagnosticId("MIST_PM_UNKNOWN_ABSTRACT_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized abstract member declaration node '{0}'.");

    /// <summary>An unrecognized concrete member declaration node.</summary>
    public static readonly DiagnosticDescriptor UnknownMember = new(
        new DiagnosticId("MIST_PM_UNKNOWN_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized member declaration node '{0}'.");

    /// <summary>A modifier on a getter accessor is not permitted.</summary>
    public static readonly DiagnosticDescriptor GetterCannotHaveModifier = new(
        new DiagnosticId("MIST_PM_GETTER_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The getter on property '{0}' cannot have a modifier.");

    /// <summary>Only 'private' is permitted as a setter accessor modifier.</summary>
    public static readonly DiagnosticDescriptor SetterModifierMustBePrivate = new(
        new DiagnosticId("MIST_PM_SETTER_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Only 'private' is permitted as a modifier on the setter of property '{0}'.");

    /// <summary>A property or indexer must declare at least one accessor.</summary>
    public static readonly DiagnosticDescriptor EmptyAccessorList = new(
        new DiagnosticId("MIST_PM_EMPTY_ACCESSORS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' must declare at least a getter or a setter.");

    /// <summary>An init accessor is not permitted on an indexer.</summary>
    public static readonly DiagnosticDescriptor IndexerCannotHaveInit = new(
        new DiagnosticId("MIST_PM_INDEXER_INIT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Indexers cannot declare init accessors.");

    /// <summary>An abstract property accessor cannot have a modifier.</summary>
    public static readonly DiagnosticDescriptor AbstractAccessorCannotHaveModifier = new(
        new DiagnosticId("MIST_PM_ABSTRACT_ACCESSOR_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Accessors on abstract property '{0}' cannot have modifiers.");

    /// <summary>An init accessor is not permitted on an abstract property.</summary>
    public static readonly DiagnosticDescriptor AbstractPropertyInitAccessor = new(
        new DiagnosticId("MIST_PM_ABSTRACT_INIT_ACCESSOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract types cannot declare init accessors on property '{0}' because abstract types cannot be instantiated.");

    // ── Modifier validation ──────────────────────────────────────────────

    /// <summary>A member cannot be both public and private.</summary>
    public static readonly DiagnosticDescriptor ConflictingAccessModifiers = new(
        new DiagnosticId("MIST_PM_CONFLICTING_ACCESS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A member cannot be both 'public' and 'private'.");

    /// <summary>Multiple substructural discipline modifiers (affine, linear, relevant) on the same declaration.</summary>
    public static readonly DiagnosticDescriptor ConflictingDisciplineModifiers = new(
        new DiagnosticId("MIST_PM_CONFLICTING_DISCIPLINE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A declaration cannot have more than one substructural discipline modifier. Specify at most one of 'affine', 'linear', or 'relevant'.");

    /// <summary>The 'async' modifier is only valid on methods.</summary>
    public static readonly DiagnosticDescriptor AsyncOnlyOnMethods = new(
        new DiagnosticId("MIST_PM_ASYNC_NOT_ALLOWED"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'async' modifier is only valid on methods.");

    /// <summary>The 'intrinsic' modifier is not valid on fields or properties.</summary>
    public static readonly DiagnosticDescriptor IntrinsicNotValidOnMember = new(
        new DiagnosticId("MIST_PM_INTRINSIC_NOT_VALID"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'intrinsic' modifier is not valid on {0}.");

    /// <summary>A non-intrinsic method on a concrete type must have a body.</summary>
    public static readonly DiagnosticDescriptor MethodMustHaveBody = new(
        new DiagnosticId("MIST_PM_METHOD_MUST_HAVE_BODY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Method '{0}' must have a body unless it is declared 'intrinsic' or is on an abstract type.");

    /// <summary>An intrinsic member must not have a body.</summary>
    public static readonly DiagnosticDescriptor IntrinsicMemberCannotHaveBody = new(
        new DiagnosticId("MIST_PM_INTRINSIC_HAS_BODY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Intrinsic member '{0}' must not have a body; use ';' to mark it as backend-provided.");

    /// <summary>An intrinsic member must be inside a type that is itself intrinsic.</summary>
    public static readonly DiagnosticDescriptor IntrinsicRequiresIntrinsicType = new(
        new DiagnosticId("MIST_PM_INTRINSIC_NOT_IN_INTRINSIC_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{0}' is not intrinsic.");

    /// <summary>An intrinsic field, property, or indexer must be declared public.</summary>
    public static readonly DiagnosticDescriptor IntrinsicMemberMustBePublic = new(
        new DiagnosticId("MIST_PM_INTRINSIC_NOT_PUBLIC"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The 'intrinsic' modifier requires the member to be declared 'public'.");

    /// <summary>An indexer cannot use auto-accessors (get; / set;) on a concrete type.</summary>
    public static readonly DiagnosticDescriptor IndexerCannotHaveAutoAccessor = new(
        new DiagnosticId("MIST_PM_INDEXER_AUTO_ACCESSOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Indexers cannot use auto-accessors; provide a body or expression for each accessor.");

    // ── Cast operator validation ─────────────────────────────────────────

    /// <summary>A cast operator must declare exactly one source parameter.</summary>
    public static readonly DiagnosticDescriptor CastOperatorMissingParameter = new(
        new DiagnosticId("MIST_PM_CAST_OP_MISSING_PARAM"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A cast operator must declare exactly one parameter (the source value).");

    /// <summary>A cast operator has more than one parameter.</summary>
    public static readonly DiagnosticDescriptor CastOperatorTooManyParameters = new(
        new DiagnosticId("MIST_PM_CAST_OP_TOO_MANY_PARAMS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A cast operator must declare exactly one parameter; additional parameters are not permitted.");

    /// <summary>An operator or cast operator must be declared static.</summary>
    public static readonly DiagnosticDescriptor OperatorMustBeStatic = new(
        new DiagnosticId("MIST_PM_OPERATOR_MUST_BE_STATIC"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Operators must be declared 'static'.");

    /// <summary>An operator has the wrong number of parameters for its kind.</summary>
    public static readonly DiagnosticDescriptor OperatorWrongParameterCount = new(
        new DiagnosticId("MIST_PM_OPERATOR_WRONG_PARAM_COUNT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Operator expects {0} parameter(s) but has {1}.");

    /// <summary>A non-abstract operator must have a body.</summary>
    public static readonly DiagnosticDescriptor OperatorMustHaveBody = new(
        new DiagnosticId("MIST_PM_OPERATOR_MUST_HAVE_BODY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Operators must have a body unless declared on an abstract type.");

    /// <summary>A member kind is not permitted on an abstract type.</summary>
    public static readonly DiagnosticDescriptor MemberNotAllowedOnAbstract = new(
        new DiagnosticId("MIST_PM_MEMBER_NOT_ALLOWED_ON_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "This member is not permitted on abstract types.");

    /// <summary>A type slot is not permitted on a concrete type.</summary>
    public static readonly DiagnosticDescriptor TypeSlotNotAllowedOnConcrete = new(
        new DiagnosticId("MIST_PM_TYPE_SLOT_NOT_ALLOWED_ON_CONCRETE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type slots are only permitted on abstract types.");

    // ── Default block validation ─────────────────────────────────────────

    /// <summary>A type declares more than one default block.</summary>
    public static readonly DiagnosticDescriptor DuplicateDefaultBlock = new(
        new DiagnosticId("MIST_PM_DUPLICATE_DEFAULT_BLOCK"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' declares more than one default block; only one is permitted per type.");

    // ── Constraint resolution ────────────────────────────────────────────

    /// <summary>A where clause names a type parameter that does not exist on the declaration.</summary>
    public static readonly DiagnosticDescriptor UnresolvedConstraintTarget = new(
        new DiagnosticId("MIST_PM_UNRESOLVED_CONSTRAINT_TARGET"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The constraint clause references type parameter '{0}', which is not declared on this type or method.");

    /// <summary>Mutually exclusive constraints are declared on the same type parameter.</summary>
    public static readonly DiagnosticDescriptor ConflictingConstraints = new(
        new DiagnosticId("MIST_PM_CONFLICTING_CONSTRAINTS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type parameter '{0}' has conflicting constraints: '{1}' and '{2}' are mutually exclusive.");

    /// <summary>The same constraint keyword appears more than once on a type parameter.</summary>
    public static readonly DiagnosticDescriptor DuplicateConstraint = new(
        new DiagnosticId("MIST_PM_DUPLICATE_CONSTRAINT"),
        DiagnosticSeverity.Warning,
        PipelineStage.SemanticModelConstruction,
        "Type parameter '{0}' has duplicate '{1}' constraint; the redundant occurrence has no effect.");

    // ── With clause resolution ───────────────────────────────────────────

    /// <summary>A this-tier with clause is not valid on a constructor.</summary>
    public static readonly DiagnosticDescriptor ThisTierOnConstructor = new(
        new DiagnosticId("MIST_PM_THIS_TIER_ON_CONSTRUCTOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A 'with {tier}' clause is not valid on a constructor; constructors always produce an owned value.");

    // ── Extension block validation ───────────────────────────────────────

    /// <summary>Multiple extension blocks in the same namespace declare a member with the same name for the same receiver type.</summary>
    public static readonly DiagnosticDescriptor DuplicateExtensionMember = new(
        new DiagnosticId("MIST_PM_DUPLICATE_EXTENSION_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "An extension member named '{0}' is already declared for type '{1}' in this namespace.");

    /// <summary>An extension property cannot use auto-accessors because extension properties have no backing field.</summary>
    public static readonly DiagnosticDescriptor ExtensionPropertyCannotHaveAutoAccessor = new(
        new DiagnosticId("MIST_PM_EXTENSION_AUTO_ACCESSOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Extension property '{0}' cannot use auto-accessors (get; / set;); extension properties have no backing field.");

    /// <summary>A local variable with the same name is already declared in this scope.</summary>
    public static readonly DiagnosticDescriptor DuplicateLocalVariable = new(
        new DiagnosticId("MIST_PM_DUPLICATE_LOCAL"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A variable named '{0}' is already declared in this scope.");

    // ── Body binding ─────────────────────────────────────────────────────

    /// <summary>A 'this' reference appears where there is no enclosing type.</summary>
    public static readonly DiagnosticDescriptor ThisOutsideType = new(
        new DiagnosticId("MIST_PM_THIS_OUTSIDE_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'this' is not valid here because there is no enclosing type. It is only valid inside an instance member of a type.");

    /// <summary>A 'this' reference appears in a static member.</summary>
    public static readonly DiagnosticDescriptor ThisInStaticMember = new(
        new DiagnosticId("MIST_PM_THIS_IN_STATIC"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'this' is not valid in a static member because a static member has no instance to refer to.");

    // ── Array literal inference ──────────────────────────────────────────

    /// <summary>An empty array literal appears where no target array type supplies the element type.</summary>
    public static readonly DiagnosticDescriptor EmptyArrayLiteralNeedsTargetType = new(
        new DiagnosticId("MIST_PM_EMPTY_ARRAY_LITERAL_NO_TARGET"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The element type of an empty array literal cannot be inferred. Annotate the target with an array type, for example 'int[] x = [];'.");

    /// <summary>The elements of an array literal have no single common type.</summary>
    public static readonly DiagnosticDescriptor ArrayLiteralNoCommonElementType = new(
        new DiagnosticId("MIST_PM_ARRAY_LITERAL_NO_COMMON_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The elements of the array literal have no common type ('{0}' and '{1}'). Annotate the target with an array type to select the element type explicitly.");

    // ── Entry points ─────────────────────────────────────────────────────
    /// <summary>
    /// A second entry point was found in an assembly that already had one. Detected as the
    /// duplicate is recorded, so the type named is the one that already claimed the
    /// assembly's entry point rather than necessarily the first in source order.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateEntryPoint = new(
        new DiagnosticId("MIST_PM_DUPLICATE_ENTRY_POINT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' declares an entry point, but '{1}' already declares one in the same assembly. A program can have only one entry point.");
}