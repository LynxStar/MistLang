#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the ownership and reference system analyzer.
/// Covers move tracking, borrow checking, lending compatibility, mutation blocking,
/// exclusivity enforcement, and owned-type contract validation.
/// </summary>
public static class OwnershipDiagnostics
{
    /// <summary>Access to a variable after it has been moved.</summary>
    public static readonly DiagnosticDescriptor UseAfterMove = new(
        new DiagnosticId("MIST_OWN_USE_AFTER_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Variable '{0}' has been moved and cannot be accessed here");

    /// <summary>A ref escapes its declaring scope.</summary>
    public static readonly DiagnosticDescriptor RefEscape = new(
        new DiagnosticId("MIST_OWN_REF_ESCAPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Reference '{0}' escapes its declaring scope");

    /// <summary>Mutation attempted while a frozen ref is outstanding.</summary>
    public static readonly DiagnosticDescriptor MutationBlocked = new(
        new DiagnosticId("MIST_OWN_MUTATION_BLOCKED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot mutate '{0}' while a frozen ref is outstanding. "
        + "Mutation is blocked until all frozen refs are returned.");

    /// <summary>Ref assigned to a field or collection (non-storable).</summary>
    public static readonly DiagnosticDescriptor NotStorable = new(
        new DiagnosticId("MIST_OWN_NOT_STORABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' variable '{1}' to field '{2}' because ref-tier references are non-storable");

    /// <summary>Multiple mutable borrows or access during ref loan.</summary>
    public static readonly DiagnosticDescriptor ExclusiveViolation = new(
        new DiagnosticId("MIST_OWN_EXCLUSIVE_VIOLATION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot access variable '{0}' while a borrowed ref is outstanding. "
        + "The owner has no access (read or write) until the ref is returned.");

    /// <summary>Lending from a tier that cannot provide the requested tier.</summary>
    public static readonly DiagnosticDescriptor IncompatibleLend = new(
        new DiagnosticId("MIST_OWN_INCOMPATIBLE_LEND"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot lend {0} variable '{1}' to {2} parameter '{3}'. "
        + "The lending pair ({0}, {2}) is not permitted.");

    /// <summary>Owned type attempts to implement abstract type.</summary>
    public static readonly DiagnosticDescriptor AbstractForbidden = new(
        new DiagnosticId("MIST_OWN_ABSTRACT_FORBIDDEN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'owned' modifier cannot be applied to abstract type '{0}'");

    /// <summary>Ownership specifier mismatch in abstract type satisfaction.</summary>
    public static readonly DiagnosticDescriptor SignatureMismatch = new(
        new DiagnosticId("MIST_OWN_SIGNATURE_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ownership mismatch: '{0}' has '{1}' but '{2}' requires '{3}'.");

    /// <summary>Non-owned specifier used on return type.</summary>
    public static readonly DiagnosticDescriptor InvalidReturnSpec = new(
        new DiagnosticId("MIST_OWN_INVALID_RETURN_SPEC"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is not valid on a return type. Only 'owned' is permitted as a return type ownership specifier.");

    /// <summary>Variable moved in one branch, accessed in another.</summary>
    public static readonly DiagnosticDescriptor ConditionalMove = new(
        new DiagnosticId("MIST_OWN_CONDITIONAL_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Variable '{0}' was moved in one branch but not all branches. "
        + "The variable cannot be accessed after the conditional.");

    /// <summary>Variable moved in loop without re-assignment.</summary>
    public static readonly DiagnosticDescriptor LoopMove = new(
        new DiagnosticId("MIST_OWN_LOOP_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Owned variable '{0}' is moved inside a loop. "
        + "The variable must be re-assigned before the next iteration or the move must be removed.");

    /// <summary>Unrestricted variable passed to ref/frozen ref parameter.</summary>
    public static readonly DiagnosticDescriptor UnrestrictedToRef = new(
        new DiagnosticId("MIST_OWN_UNRESTRICTED_TO_REF"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot pass unrestricted variable '{0}' to {1} parameter '{2}'. "
        + "Unrestricted references cannot provide the guarantees required by {1}.");

    /// <summary>Multiple ownership specifiers on same declaration.</summary>
    public static readonly DiagnosticDescriptor DuplicateSpecifier = new(
        new DiagnosticId("MIST_OWN_DUPLICATE_SPECIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Multiple ownership specifiers found on declaration '{0}'. "
        + "Only one ownership specifier is permitted per declaration.");

    /// <summary>
    /// Mutable method called on a non-owned type held in an owned binding.
    /// Non-owned types appear readonly when held in an owned binding, so only readonly methods are visible.
    /// </summary>
    public static readonly DiagnosticDescriptor MutableHidden = new(
        new DiagnosticId("MIST_OWN_MUTABLE_HIDDEN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot call mutable method '{0}' on '{1}': non-owned types appear readonly when held in an "
        + "owned binding, so only readonly methods are visible. Declare '{1}' as 'owned', or assign the field "
        + "directly from an owned source.");

    /// <summary>
    /// Owned destination assigned a non-owned, non-clonable source.
    /// An owned destination requires a uniquely-owned source.
    /// </summary>
    public static readonly DiagnosticDescriptor RequiresOwned = new(
        new DiagnosticId("MIST_OWN_REQUIRES_OWNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Owned destination '{0}' cannot be assigned from '{1}': an owned destination "
        + "requires a uniquely-owned source — a constructor call, an owned-returning call, a move from an existing "
        + "owned binding, or a clonable source. Move an owned value, construct a fresh value, or make the source type "
        + "implement 'IClonable<T>'.");

    /// <summary>
    /// An implicit clone is needed but the source type does not implement <c>IClonable&lt;T&gt;</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor NotClonable = new(
        new DiagnosticId("MIST_OWN_NOT_CLONABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly clone source of type '{0}' into an owned destination because '{0}' does not "
        + "implement 'IClonable<{0}>'. Make '{0}' implement 'IClonable<T>' so it can be cloned, or supply "
        + "a uniquely-owned source instead.");

    /// <summary>
    /// An owned type's method or constructor assigns a field from a non-owned source,
    /// violating the owned-type cooperation contract.
    /// </summary>
    public static readonly DiagnosticDescriptor ContractViolation = new(
        new DiagnosticId("MIST_OWN_CONTRACT_VIOLATION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method '{0}' violates the owned-type cooperation contract: it assigns field '{1}' from the "
        + "non-owned source '{2}'. Owned types may only populate fields from owned sources — make the "
        + "parameter 'owned', clone the source, or construct a fresh value.");

    /// <summary>
    /// The <c>owned</c> binding specifier is applied to a substructural
    /// (<c>linear</c>/<c>affine</c>/<c>relevant</c>) type.
    /// </summary>
    public static readonly DiagnosticDescriptor SubstructuralBinding = new(
        new DiagnosticId("MIST_OWN_SUBSTRUCTURAL_BINDING"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'owned' binding specifier cannot be applied to binding '{0}' of substructural type "
        + "'{1}': substructural ('linear'/'affine'/'relevant') types manage their own usage discipline via drop "
        + "semantics and cannot be combined with 'owned' binding tracking. Remove the 'owned' specifier and rely on the "
        + "type's substructural discipline.");

    /// <summary>
    /// Explicit <c>with {tier}</c> annotation under-declares the body's requirements.
    /// The method body requires a higher tier than what was declared.
    /// </summary>
    public static readonly DiagnosticDescriptor ThisTierInsufficient = new(
        new DiagnosticId("MIST_OWN_THIS_TIER_INSUFFICIENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method '{0}' declares 'with {1}' but the body requires at least "
        + "'{2}' access to 'this' — owned-field operations in the body demand a higher tier.");

    /// <summary>
    /// Call-site receiver does not satisfy the method's inferred or declared this-tier.
    /// </summary>
    public static readonly DiagnosticDescriptor ThisTierMismatch = new(
        new DiagnosticId("MIST_OWN_THIS_TIER_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot call '{0}' which requires '{1}' access to 'this' — the receiver "
        + "'{2}' only provides '{3}' access.");

    /// <summary>
    /// Ownership inference cannot find a consistent tier for a var binding.
    /// </summary>
    public static readonly DiagnosticDescriptor InferenceFailure = new(
        new DiagnosticId("MIST_OWN_INFERENCE_FAILURE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot infer a consistent ownership tier for '{0}': tier '{1}' conflicts with "
        + "{2} which requires '{3}'. Add an explicit ownership specifier to resolve the ambiguity.");

    /// <summary>
    /// Move, borrow, freeze, or readonly-ref of an owned field through a shared parent.
    /// Extracting data from an owned field requires an owned binding to the parent.
    /// </summary>
    public static readonly DiagnosticDescriptor SharedExtract = new(
        new DiagnosticId("MIST_OWN_SHARED_EXTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot extract from owned field '{0}' through shared binding "
        + "'{1}' — extracting data from an owned field requires an owned binding to the parent.");

    /// <summary>
    /// Read of an owned field through a shared parent when the type does not implement IClonable.
    /// </summary>
    public static readonly DiagnosticDescriptor SharedReadNotClonable = new(
        new DiagnosticId("MIST_OWN_SHARED_READ_NOT_CLONABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot read owned field '{0}' through shared binding "
        + "'{1}' — the field's type '{2}' does not implement 'IClonable<{2}>' and cannot be "
        + "moved out without an owned binding to the parent.");

    /// <summary>
    /// Static field marked <c>owned</c>. Ownership semantics require an instance-level owner.
    /// </summary>
    public static readonly DiagnosticDescriptor StaticFieldOwned = new(
        new DiagnosticId("MIST_OWN_STATIC_FIELD_OWNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'owned' modifier cannot be applied to static field '{0}' — "
        + "ownership semantics require an instance-level owner.");

    /// <summary>
    /// A non-owned reference type field without <c>IClonable&lt;T&gt;</c> in an owned type
    /// violates the field-type restriction.
    /// </summary>
    public static readonly DiagnosticDescriptor FieldTypeViolation = new(
        new DiagnosticId("MIST_OWN_FIELD_TYPE_VIOLATION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' of type '{1}' violates the owned-type field-type restriction: "
        + "all fields of an owned type must be owned types, implement 'IClonable<T>', or be value types "
        + "to guarantee full graph ownership.");

    // ── Drop analyzer descriptors ────────────────────────────────────────

    /// <summary>Use-after-drop/move on a linear value.</summary>
    public static readonly DiagnosticDescriptor DropUseAfterMove = new(
        new DiagnosticId("MIST_DROP_USE_AFTER_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Use-after-drop on an affine value.</summary>
    public static readonly DiagnosticDescriptor DropUseAfterDrop = new(
        new DiagnosticId("MIST_DROP_USE_AFTER_DROP"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Non-owned container declares an affine or linear field.</summary>
    public static readonly DiagnosticDescriptor DropFieldNotOwned = new(
        new DiagnosticId("MIST_DROP_FIELD_NOT_OWNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Field Drop() called outside of the containing type's Drop() method.</summary>
    public static readonly DiagnosticDescriptor DropFieldOutsideContainer = new(
        new DiagnosticId("MIST_DROP_FIELD_OUTSIDE_CONTAINER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>Conflicting substructural/ownership type modifiers.</summary>
    public static readonly DiagnosticDescriptor DropConflictingModifiers = new(
        new DiagnosticId("MIST_DROP_CONFLICTING_MODIFIERS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>A Drop() method declared with one or more parameters.</summary>
    public static readonly DiagnosticDescriptor DropParameters = new(
        new DiagnosticId("MIST_DROP_PARAMETERS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    // ── Frame-escape analyzer descriptors ────────────────────────────────

    /// <summary>A stackalloc-backed span is returned from a method.</summary>
    public static readonly DiagnosticDescriptor StackBackedReturn = new(
        new DiagnosticId("MIST2008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");

    /// <summary>A stackalloc-backed span escapes its frame via a store.</summary>
    public static readonly DiagnosticDescriptor StackBackedEscape = new(
        new DiagnosticId("MIST2009"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0}");
}