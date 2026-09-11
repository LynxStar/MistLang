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

    /// <summary>
    /// A name in expression position resolved to nothing in any reachable scope. Covers a misspelling,
    /// a name not in scope, and an instance member named from a static context -- the last of which is
    /// excluded from the lookup rather than matched and rejected, so it arrives here as an ordinary
    /// miss.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvedIdentifier = new(
        new DiagnosticId("MIST_PM_UNRESOLVED_IDENTIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The name '{0}' could not be found in this scope.");

    /// <summary>
    /// <c>await</c> applied to something that is not a task. A language rule rather than a target one:
    /// the operand has no result to unwrap, which is true of Mist before any backend sees it.
    /// </summary>
    public static readonly DiagnosticDescriptor AwaitOfNonTask = new(
        new DiagnosticId("MIST_PM_AWAIT_OF_NON_TASK"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'await' requires a task, but '{0}' is not one.");

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

    /// <summary>
    /// A type alias's expansion reaches itself (e.g., <c>type A = B; type B = A;</c>), so no
    /// finite target exists. Reported once, at the declaration where the on-demand build
    /// re-entered itself; the alias resolves to an error type to stop the recursion.
    /// </summary>
    public static readonly DiagnosticDescriptor CyclicTypeAlias = new(
        new DiagnosticId("MIST_PM_CYCLIC_TYPE_ALIAS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type alias '{0}' forms a cycle.");

    /// <summary>A qualified member access named nothing on the receiver's type or its bases.</summary>
    public static readonly DiagnosticDescriptor UnresolvedMember = new(
        new DiagnosticId("MIST_PM_UNRESOLVED_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' has no member named '{1}'.");

    /// <summary>
    /// A member name matched two different kinds of declaration -- a field and a method, say -- so
    /// there is no single thing it could mean.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousMember = new(
        new DiagnosticId("MIST_PM_AMBIGUOUS_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The member '{1}' on '{0}' is ambiguous between {2} declarations of different kinds.");

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

    /// <summary>
    /// <c>field</c> named where there is no property whose storage it could mean. The grammar admits
    /// it in any expression position, so the placement is refused here rather than left to a backend
    /// that would have nothing to emit for it.
    /// </summary>
    public static readonly DiagnosticDescriptor FieldKeywordOutsideAccessor = new(
        new DiagnosticId("MIST_PM_FIELD_OUTSIDE_ACCESSOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'field' is only meaningful inside a property accessor, where it names that property's storage.");

    /// <summary>
    /// A member shares its enclosing type's name. Refused for two independent reasons: C# rejects
    /// the declaration as CS0542, so it cannot be emitted; and inside the type the bare name shadows
    /// the type, so a type position there resolves to the member instead.
    /// </summary>
    public static readonly DiagnosticDescriptor MemberNameMatchesEnclosingType = new(
        new DiagnosticId("MIST_PM_MEMBER_NAME_IS_TYPE_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A member cannot have the same name as its enclosing type '{0}'. Rename the member.");

    /// <summary>
    /// A pattern tests a union-typed scrutinee for a type no constituent of it can hold.
    /// <para>
    /// Reported by construction rather than deferred because it decides whether emission has anything
    /// to write: narrowing a union goes through the carrier's accessor for the matching constituent, and
    /// a type belonging to none names no accessor. So this is not "a test that will always fail" -- it is
    /// a test with no lowering.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor PatternTypeNotAConstituent = new(
        new DiagnosticId("MIST_PM_PATTERN_TYPE_NOT_A_CONSTITUENT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not one of the types '{1}' can hold, so this pattern can never match.");

    /// <summary>
    /// A switch expression declares no arms. Reported rather than deferred because an expression must
    /// have a type and there is no arm to take one from: the grammar admits <c>x switch { }</c>, and an
    /// expression that can never produce a value has nothing for the model to say about it.
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchExpressionNeedsAnArm = new(
        new DiagnosticId("MIST_PM_SWITCH_NO_ARMS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A switch expression must declare at least one arm, because its type comes from what its arms produce.");

    /// <summary>
    /// A switch expression's arms produce different types and no target type says which one is wanted.
    /// <para>
    /// Reported rather than widened for the reason given on
    /// <see cref="ArrayLiteralNoCommonElementType"/>: there is no conversion or numeric promotion
    /// infrastructure to compute a best common type yet, so guessing would be worse than asking.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchArmsNoCommonType = new(
        new DiagnosticId("MIST_PM_SWITCH_ARMS_NO_COMMON_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The arms of the switch expression have no common type ('{0}' and '{1}'). Annotate the target with the type you want.");

    /// <summary>
    /// A pattern under <c>or</c> or <c>not</c> introduces a binding. Matching such a pattern does not
    /// establish the binding -- a disjunction proves only that SOME operand matched, and a negation
    /// proves that its operand did not -- so there is no value for the name to hold.
    /// <para>
    /// Reported rather than deferred to an analyzer because the model would otherwise be incoherent:
    /// the local would be declared in the guarded block's scope while emission wrote no C# pattern to
    /// produce it, so a branch reading the name would emit C# naming a variable that does not exist.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor PatternBindingNotEstablished = new(
        new DiagnosticId("MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The pattern variable '{0}' cannot be introduced inside 'or' or 'not', because matching there does not establish a value for it.");

    /// <summary>
    /// A property pattern's subpattern names something other than a field or a property. Its sibling
    /// <see cref="InitializerTargetNotAssignable"/> is worded for assignment; this one is worded for
    /// matching, since a method or a nested type holds no value for a subpattern to be tested against.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternSubpatternNotStorage = new(
        new DiagnosticId("MIST_PM_PATTERN_SUBPATTERN_NOT_STORAGE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{1}' on '{0}' is not a field or a property, so a property pattern cannot match against it.");

    /// <summary>
    /// A tuple pattern spells a different number of elements than the tuple it matches against holds.
    /// Reported rather than deferred because the arity is what pairs each element pattern with the
    /// element type it narrows, so a mismatch leaves every position after the first typed by nothing.
    /// </summary>
    public static readonly DiagnosticDescriptor TuplePatternArityMismatch = new(
        new DiagnosticId("MIST_PM_TUPLE_PATTERN_ARITY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The pattern spells {2} elements but '{0}' holds {1}, so this pattern can never match.");

    /// <summary>
    /// A tuple pattern matches against a scrutinee that is neither a tuple nor <c>unknown</c>. Mist has
    /// no user-declared deconstruction, so unlike C# -- which would look for a <c>Deconstruct</c> method
    /// -- there is nothing for the element positions to be read out of.
    /// </summary>
    public static readonly DiagnosticDescriptor TuplePatternScrutineeNotATuple = new(
        new DiagnosticId("MIST_PM_TUPLE_PATTERN_SCRUTINEE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not a tuple, so a tuple pattern has no elements to match against it.");

    /// <summary>
    /// A range was used to index a receiver that yields no sub-sequence. Reported rather than answered
    /// with an error type, which is what the INDEX case still does: an index that resolves nothing
    /// names an indexer that might simply be missing, while a range that resolves nothing has no C# to
    /// write at all, so leaving it silent would emit an element access typed as an error.
    /// </summary>
    public static readonly DiagnosticDescriptor ReceiverNotSliceable = new(
        new DiagnosticId("MIST_PM_RECEIVER_NOT_SLICEABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' cannot be sliced by a range. Only an array or a string yields a sub-sequence.");

    /// <summary>
    /// A list pattern matches against a scrutinee that yields no elements. Mist has no user-declared
    /// deconstruction, so unlike a C# positional pattern there is nothing for the element positions to
    /// be read out of, and the pattern's own arity would have nothing to constrain.
    /// </summary>
    public static readonly DiagnosticDescriptor ListPatternScrutineeNotIndexable = new(
        new DiagnosticId("MIST_PM_LIST_PATTERN_SCRUTINEE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' cannot be indexed, so a list pattern has no elements to match against it.");

    /// <summary>
    /// A list pattern spells more than one slice. The minimum length a list pattern requires is its
    /// fixed element count, and which elements are counted from the front and which from the back is
    /// decided by where the single slice sits -- two slices leave that split undetermined, so the
    /// pattern states no length constraint at all rather than a weaker one.
    /// </summary>
    public static readonly DiagnosticDescriptor ListPatternMultipleSlices = new(
        new DiagnosticId("MIST_PM_LIST_PATTERN_MULTIPLE_SLICES"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A list pattern can spell at most one '..' slice, because the slice is what divides the elements counted from the front from those counted from the back.");

    /// <summary>
    /// A function type names no type arguments at all. The result occupies the last slot, so there is no
    /// spelling of a function type without one -- <c>func&lt;bool&gt;</c> is the zero-parameter form.
    /// </summary>
    public static readonly DiagnosticDescriptor FunctionTypeNeedsAResult = new(
        new DiagnosticId("MIST_PM_FUNCTION_TYPE_NO_RESULT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A function type must name its result as the last type argument, for example 'func<bool>' or 'func<int, bool>'.");

    /// <summary>A lambda declares a different number of parameters than the function type it is assigned to.</summary>
    public static readonly DiagnosticDescriptor LambdaArityMismatch = new(
        new DiagnosticId("MIST_PM_LAMBDA_ARITY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' takes {1} parameters but the lambda declares {2}.");

    /// <summary>
    /// A lambda parameter is neither annotated nor supplied a type by a target. Reported rather than
    /// inferred from the body: a parameter's type is what the body is read AGAINST, so there is nothing
    /// to infer it from without circularity.
    /// </summary>
    public static readonly DiagnosticDescriptor LambdaParameterNeedsAType = new(
        new DiagnosticId("MIST_PM_LAMBDA_PARAMETER_NO_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type of lambda parameter '{0}' cannot be inferred. Annotate it, or assign the lambda to a function type.");

    /// <summary>The return statements of a block-bodied lambda produce different types.</summary>
    public static readonly DiagnosticDescriptor LambdaReturnsNoCommonType = new(
        new DiagnosticId("MIST_PM_LAMBDA_RETURNS_NO_COMMON_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The lambda's returns have no common type ('{0}' and '{1}'). Annotate the target with a function type to select the result.");

    /// <summary>A call on a function-typed value passes a different number of arguments than it takes.</summary>
    public static readonly DiagnosticDescriptor FunctionCallArityMismatch = new(
        new DiagnosticId("MIST_PM_FUNCTION_CALL_ARITY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' takes {1} arguments but {2} were passed.");

    /// <summary>A deconstruction declares a different number of targets than the tuple it reads holds.</summary>
    public static readonly DiagnosticDescriptor DeconstructionArityMismatch = new(
        new DiagnosticId("MIST_PM_DECONSTRUCTION_ARITY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The deconstruction declares {2} targets but '{0}' holds {1} elements.");

    /// <summary>
    /// A deconstruction reads from something that is not a tuple. Mist has no user-declared
    /// deconstruction, so unlike C# -- which would look for a <c>Deconstruct</c> method -- a
    /// non-tuple has no elements to distribute among the targets.
    /// </summary>
    public static readonly DiagnosticDescriptor DeconstructionSourceNotATuple = new(
        new DiagnosticId("MIST_PM_DECONSTRUCTION_SOURCE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not a tuple, so it cannot be deconstructed.");

    /// <summary>A local variable with the same name is already declared in this scope.</summary>
    public static readonly DiagnosticDescriptor DuplicateLocalVariable = new(
        new DiagnosticId("MIST_PM_DUPLICATE_LOCAL"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A variable named '{0}' is already declared in this scope.");

    public static readonly DiagnosticDescriptor DuplicateLocalFunction = new(
        new DiagnosticId("MIST_PM_DUPLICATE_LOCAL_FUNCTION"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A local function named '{0}' is already declared in this scope.");

    /// <summary>
    /// A modifier a local function cannot carry: it has no accessibility to seal, no <c>this</c> of
    /// its own to be readonly for, no discipline to declare, and no body the compiler supplies.
    /// </summary>
    public static readonly DiagnosticDescriptor LocalFunctionModifier = new(
        new DiagnosticId("MIST_PM_LOCAL_FUNCTION_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A local function cannot be declared '{0}'.");

    public static readonly DiagnosticDescriptor LocalFunctionGeneric = new(
        new DiagnosticId("MIST_PM_LOCAL_FUNCTION_GENERIC"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Local function '{0}' cannot declare type parameters.");

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

    /// <summary>
    /// A <c>foreach</c> over <c>var</c> whose collection is not a sequence, so there is no element
    /// type to give the loop variable. Reported here because the binder cannot finish the loop
    /// without one: the variable is declared into the body's scope, and every read of it downstream
    /// resolves against whatever type it was given.
    /// </summary>
    public static readonly DiagnosticDescriptor ForEachElementNotInferable = new(
        new DiagnosticId("MIST_PM_FOREACH_ELEMENT_NOT_INFERABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Cannot infer the element type of '{0}' because it is not an array and conforms to no IEnumerable. Iterate a sequence, or state the element type in place of 'var'.");

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

    // ── Target-typed new ─────────────────────────────────────────────────

    /// <summary>
    /// A <c>new(...)</c> appears where nothing supplies the type to construct.
    /// <para>
    /// Also reported where several overloads take this many arguments, because the parameter that
    /// would supply the target cannot be picked without the argument types and this argument is one
    /// of them. Hence a message asking for the type rather than one claiming none exists.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor TargetTypedNewNeedsTargetType = new(
        new DiagnosticId("MIST_PM_TARGET_TYPED_NEW_NO_TARGET"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type of 'new(...)' cannot be determined at this position. Name it, as in 'new Point(...)'.");

    /// <summary>A bare <c>default</c> appears where nothing supplies the type to default.</summary>
    public static readonly DiagnosticDescriptor DefaultNeedsTargetType = new(
        new DiagnosticId("MIST_PM_DEFAULT_NO_TARGET"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type of 'default' cannot be determined at this position. Name it, as in 'default(int)'.");

    /// <summary>A <c>new(...)</c> is targeted at something no constructor can produce.</summary>
    public static readonly DiagnosticDescriptor TargetTypeNotConstructible = new(
        new DiagnosticId("MIST_PM_TARGET_TYPE_NOT_CONSTRUCTIBLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' cannot be constructed, so 'new(...)' has nothing to create here. Only a class or a struct can be.");

    // ── Object initializers ──────────────────────────────────────────────

    public static readonly DiagnosticDescriptor InitializerTargetNotAssignable = new(
        new DiagnosticId("MIST_PM_INITIALIZER_TARGET_NOT_ASSIGNABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{1}' on '{0}' is not a field or a property, so an initializer block cannot assign to it.");

    /// <summary>
    /// An initializer block element is a bare expression, which is C#'s collection-initializer form.
    /// Refused rather than passed through: it stands for a call to an <c>Add</c> overload nobody
    /// resolved, so emitting it would hand Roslyn a member the model never chose.
    /// </summary>
    public static readonly DiagnosticDescriptor CollectionInitializerNotSupported = new(
        new DiagnosticId("MIST_PM_COLLECTION_INITIALIZER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "An initializer block element must name a member and assign to it, as in 'Count = 1'. Collection initializers are not supported by the experimental pipeline; add the elements after construction.");

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

    /// <summary>
    /// Two assemblies in the same compilation share a name. An assembly's name is how a Mist
    /// program refers to it, so two of them sharing one leaves references between projects
    /// ambiguous with no way for the user to disambiguate.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateAssemblyName = new(
        new DiagnosticId("MIST_PM_DUPLICATE_ASSEMBLY_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Assembly name '{0}' is used by multiple projects in this compilation. Each referenced project must have a unique name.");

    /// <summary>
    /// A call supplies a number of arguments no overload of the named member takes. Unlike every other
    /// mismatch, counting parameters needs no conversion relation, so this is decidable outright.
    /// </summary>
    public static readonly DiagnosticDescriptor NoApplicableOverload = new(
        new DiagnosticId("MIST_PM_NO_APPLICABLE_OVERLOAD"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "There is no overload of {0} accepting {1} argument(s). Declared arities: {2}.");

    /// <summary>
    /// Several overloads tie for a call, so choosing between them would be arbitrary.
    /// <para>
    /// Reported only among candidates already established as APPLICABLE, which is what makes a tie
    /// safe to report from a partial conversion relation: it rests on a positive fact about each
    /// candidate rather than on the absence of a conversion nobody looked for. A call the relation
    /// cannot place at all is <see cref="UnresolvableOverload"/> instead, which is an absence rather
    /// than a tie.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousOverload = new(
        new DiagnosticId("MIST_PM_AMBIGUOUS_OVERLOAD"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The call to {0} is ambiguous. These overloads are equally applicable: {1}. Cast an argument to choose one.");

    /// <summary>
    /// A method group reached a position that does not consume it. A group has two consumers, an
    /// invocation and a conversion to a function type, and only the first is built.
    /// <para>
    /// One message covers two different situations because separating them needs the target type this
    /// pipeline does not propagate yet: a group where no function type could have been meant, which
    /// is an error however much of the compiler gets built, and a group whose conversion is merely
    /// unimplemented. So it states the rule rather than attributing the fault to either side.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor MethodGroupNotConverted = new(
        new DiagnosticId("MIST_PM_METHOD_GROUP_NOT_CONVERTED"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' names a method group, which is only valid where it is invoked or converted to a function type. Conversion to a function type is not yet supported by the experimental pipeline.");

    /// <summary>
    /// Several overloads take the right number of arguments and no conversion could be established
    /// to any of them.
    /// <para>
    /// Reported rather than guessed. A partial conversion relation licenses declining to refuse a
    /// program, which is not the same licence as inventing a selection: with every candidate
    /// eliminated the relation has contributed nothing to choose on, so picking one would record a
    /// call the program does not make and hand the emitter a member the user never named.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableOverload = new(
        new DiagnosticId("MIST_PM_UNRESOLVABLE_OVERLOAD"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The call to {0} matches no overload. No conversion is known from the given arguments to any of: {1}.");

    /// <summary>
    /// Refused rather than left open, unlike the two failures resting on the partial conversion
    /// relation. An undetermined parameter is a missing TYPE, not a missing preference: binding anyway
    /// emits C# naming a type parameter where a type belongs, which puts the error on a generated file
    /// instead of on the call.
    /// </summary>
    public static readonly DiagnosticDescriptor TypeArgumentsNotInferred = new(
        new DiagnosticId("MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Cannot infer the type arguments of {0} from the arguments. State all of them explicitly at the call: {1}.");

    public static readonly DiagnosticDescriptor TypeArgumentCountMismatch = new(
        new DiagnosticId("MIST_PM_TYPE_ARGUMENT_COUNT_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The call to {0} states the wrong number of type arguments. Declared type parameter counts: {1}.");

    // ── The unknown top type ─────────────────────────────────────────────

    /// <summary>
    /// <c>unknown</c> is the declared type of writable storage, directly or through a type
    /// argument, union constituent or tuple element.
    /// <para>
    /// Storage is banned because it is the one position that would let a value LEAVE the top type
    /// without a pattern: write an <c>int</c> into an <c>unknown</c>-typed slot and read it back
    /// through a concretely-typed view of the same slot, and the narrowing rule has been bypassed
    /// without any assignment having broken it.
    /// </para>
    /// <para>
    /// A getter-only property is refused along with the rest. The ban is on the declaration, not on
    /// the presence of a setter, because it is the declared type that decides what the slot admits
    /// -- and a property that is read-only today is one edit away from not being.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownAtStoragePosition = new(
        new DiagnosticId("MIST_PM_UNKNOWN_AT_STORAGE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "{0} cannot have type '{1}': 'unknown' is not permitted as the declared type of writable storage. Use a concrete type, or accept 'unknown' as a parameter and pattern-match it.");

    // ── Retroactive conformance ──────────────────────────────────────────

    /// <summary>
    /// An abstract is excluded too: it declares its conformances in its own base list, and no other
    /// kind of type has members to map. None of these four counts slots -- coverage is a judgement on
    /// a completed model.
    /// </summary>
    public static readonly DiagnosticDescriptor ConformanceSourceNotConcrete = new(
        new DiagnosticId("MIST_PM_CONFORMANCE_NOT_CONCRETE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' cannot be declared to conform retroactively: only a class or a struct can be the subject of a with block.");

    public static readonly DiagnosticDescriptor ConformanceTargetNotAbstract = new(
        new DiagnosticId("MIST_PM_CONFORMANCE_NOT_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not an abstract type, so '{1}' cannot be declared to conform to it.");

    /// <summary>
    /// Reported at construction because it stops the variant getting a value at all, which every
    /// later stage depends on having -- not because a finished program is judged wrong on inspection.
    /// </summary>
    public static readonly DiagnosticDescriptor EnumVariantValueNotInferable = new(
        new DiagnosticId("MIST_PM_ENUM_VALUE_NOT_INFERABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Enum variant '{0}' needs an explicit value: none can be inferred over base type '{1}'. Only a string base, which uses the variant name, and an integral base, which counts up from the previous variant, can supply one.");

    /// <summary>
    /// Counting on needs the previous variant's constant, and a written value the binder cannot fold
    /// -- a const reference, arithmetic -- withholds it. Refused rather than guessed: the classic
    /// pipeline restarts counting at zero here, silently merging variants the author believes are
    /// distinct, since variants sharing a value are aliases under this lowering.
    /// </summary>
    public static readonly DiagnosticDescriptor EnumVariantValueNotCountable = new(
        new DiagnosticId("MIST_PM_ENUM_VALUE_NOT_COUNTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Enum variant '{0}' needs an explicit value: the previous variant's value is not a compile-time constant this phase can count on from.");

    /// <summary>
    /// The compiler's own arithmetic walked outside the base type. Reported in Mist terms because the
    /// author wrote no value here -- an overflow error against generated C# would name a number they
    /// never typed.
    /// </summary>
    public static readonly DiagnosticDescriptor EnumVariantValueOutOfRange = new(
        new DiagnosticId("MIST_PM_ENUM_VALUE_OUT_OF_RANGE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Enum variant '{0}' would take inferred value {1}, which does not fit base type '{2}'. Give it an explicit value, or widen the base type.");

    public static readonly DiagnosticDescriptor ConformanceUnresolvedSlot = new(
        new DiagnosticId("MIST_PM_CONFORMANCE_UNRESOLVED_SLOT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract type '{0}' declares no member named '{1}'.");

    /// <summary>
    /// One descriptor for every way a mapping's two ends fail to pair: the remedy is the same in
    /// each, and the message names both ends.
    /// </summary>
    public static readonly DiagnosticDescriptor ConformanceMappingUnsatisfied = new(
        new DiagnosticId("MIST_PM_CONFORMANCE_MAPPING_UNSATISFIED"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "No member named '{0}' on '{1}' can answer slot '{2}' of '{3}'. A mapping needs a member of the same kind, and for a method one taking the same number of parameters.");

    /// <summary>
    /// An <c>as</c> names a target the source can never reach, in either direction. Reported by
    /// construction rather than deferred because the classification is what gives the expression its
    /// TYPE: an unreachable target leaves nothing to answer with, neither the bare type a provable
    /// conversion yields nor the optional a checked one does.
    /// <para>
    /// A void end reaches here too, rather than through a message of its own. Nothing converts to or from
    /// the unit type, so "no relationship" is already the true answer and a second descriptor would only
    /// word it more specifically.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor AsConversionNoRelationship = new(
        new DiagnosticId("MIST_PM_AS_NO_RELATIONSHIP"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "No conversion relates '{0}' to '{1}', so 'as' has no result type to produce. One must contain the other, or the source must be a union or unknown holding it.");

    /// <summary>
    /// A type parameter list declares more than one bracket-free pack. Two of them cannot divide a
    /// flat argument list between themselves -- any split would do -- so the list is ambiguous by
    /// construction rather than merely unsupported. Brackets are the way to declare a second pack.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleUnbracketedPacks = new(
        new DiagnosticId("MIST_VAR_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is a second bracket-free pack after '{1}'. Only one pack can absorb a flat argument list; declare this one as 'Type[]:{0}' and supply it in brackets.");

    /// <summary>
    /// A dependent parameter's domain is outside the set whose values can carry type identity. The
    /// line is drawn at equality: a construction is identified BY its argument, so a domain whose
    /// equality is partial or approximate would make type identity the same, which is why the
    /// floating-point types are out however convenient they look.
    /// </summary>
    public static readonly DiagnosticDescriptor DisallowedDependentDomain = new(
        new DiagnosticId("MIST_DT_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed.");

    /// <summary>
    /// A flat type-argument list cannot reach the fixed slots even with the pack absorbing nothing.
    /// A pack makes the count flexible UPWARD only: the slots around it still each need an
    /// argument, so there is a floor and this is it.
    /// </summary>
    public static readonly DiagnosticDescriptor TooFewTypeArgumentsForPack = new(
        new DiagnosticId("MIST_VAR_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' needs at least {1} type arguments to fill the slots around its pack, but {2} were given.");

    /// <summary>
    /// A member name that a spread already owns. A spread declares a FAMILY -- the aggregate and
    /// every indexed projection -- and how many of the latter there are is not known where the
    /// declaration is written, so the whole prefixed namespace has to be reserved rather than the
    /// names that happen to exist for one instantiation.
    /// </summary>
    public static readonly DiagnosticDescriptor SpreadStemCollision = new(
        new DiagnosticId("MIST_VAR_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' collides with the spread member '{1}', which owns that name and every '{1}' followed by an index.");

    /// <summary>
    /// A homogeneous `params T[]` that is not the last parameter. The call site supplies its
    /// arguments by running off the end of the fixed ones, which only the last slot can do -- where
    /// a value PACK binds inward from both ends and so may sit anywhere.
    /// <para>
    /// A RULE OF THE LANGUAGE, so it is decided here and not by whatever the program is emitted
    /// into. The declaration is not well-formed Mist, which stays true when the backend is not C#;
    /// leaving it to the target would mean each new backend rediscovering the same rule, and only
    /// for as long as the target happened to share it.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor ParamsArrayNotLast = new(
        new DiagnosticId("MIST_VAR_0012"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is declared 'params' but is not the last parameter. A 'params' array collects the arguments after the fixed ones, so nothing may follow it.");

    /// <summary>
    /// A spread whose expansion is not known. The suffix promises a definite number of arguments,
    /// so an operand whose arity is not fixed cannot stand where arguments go -- and the promise is
    /// what the call was checked against, which is why this is refused rather than deferred.
    /// </summary>
    public static readonly DiagnosticDescriptor UnprovableSpread = new(
        new DiagnosticId("MIST_VAR_0011"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' cannot be spread here: its length is not known at this point, so the number of arguments it stands for is not either.");

    /// <summary>
    /// A projected anonymous member that ends in no name, as <c>new { f() }</c> does. The member has
    /// to be called something, and a projection says what only by ending in the name it borrows.
    /// </summary>
    public static readonly DiagnosticDescriptor AnonymousMemberNotNameable = new(
        new DiagnosticId("MIST_PM_ANONYMOUS_MEMBER_NOT_NAMEABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "This anonymous type member has no name. Write one with 'Name = ', or project something that ends in a name.");

}