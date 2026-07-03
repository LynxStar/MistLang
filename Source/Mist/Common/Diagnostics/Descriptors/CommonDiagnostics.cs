#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors shared across multiple compiler areas.
/// Covers standard library loading, SDK resolution, namespace/type resolution,
/// interop projection, target-typed new expressions, void type restrictions,
/// default keyword semantics, definite assignment, and associated type validation.
/// </summary>
public static class CommonDiagnostics
{
    // ── Standard Library loading diagnostics ─────────────────────────────

    /// <summary>Stdlib file missing, unreadable, or the stdlib directory is absent.</summary>
    public static readonly DiagnosticDescriptor StdlibParse = new(
        new DiagnosticId("MIST_STDLIB_PARSE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to load Standard Library source: {0}: {1}");

    /// <summary>
    /// The Standard Library has not been loaded (or a required stdlib type is
    /// absent from the loaded set) at a point in the pipeline that requires it.
    /// Primitive keyword resolution and related stdlib-dependent machinery cannot
    /// proceed without the corresponding stdlib type.
    /// </summary>
    public static readonly DiagnosticDescriptor StdlibMissing = new(
        new DiagnosticId("MIST_STDLIB_MISSING"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Standard Library type '{0}' is not present in the semantic model.");

    // ── SDK diagnostics ──────────────────────────────────────────────────

    /// <summary>The .NET SDK installation could not be located.</summary>
    public static readonly DiagnosticDescriptor SdkNotFound = new(
        new DiagnosticId("MIST_SDK_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The .NET SDK installation could not be located. Searched: 'dotnet --list-sdks' and well-known paths (/usr/share/dotnet, C:\\Program Files\\dotnet, ~/.dotnet).");

    /// <summary>The target framework reference path does not exist in the SDK.</summary>
    public static readonly DiagnosticDescriptor SdkNoFramework = new(
        new DiagnosticId("MIST_SDK_NO_FRAMEWORK"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The target framework '{0}' reference assemblies were not found in the SDK at '{1}'.");

    // ── Namespace and type resolution diagnostics ────────────────────────

    /// <summary>A <c>using</c> directive references a namespace that does not exist.</summary>
    public static readonly DiagnosticDescriptor NamespaceNotFound = new(
        new DiagnosticId("MIST_NS_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Namespace '{0}' could not be found.");

    /// <summary>A type name is ambiguous between multiple imported CLR namespaces.</summary>
    public static readonly DiagnosticDescriptor TypeAmbiguous = new(
        new DiagnosticId("MIST_TYPE_AMBIGUOUS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type name '{0}' is ambiguous between namespaces '{1}' and '{2}'.");

    // ── Interop diagnostics ──────────────────────────────────────────────

    // ── Framework reference resolution diagnostics ────────────────────────

    /// <summary>
    /// An enabled framework reference is a known shared framework but its
    /// reference pack is not installed for the target framework.
    /// </summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceNotInstalled = new(
        new DiagnosticId("MIST_FWREF_001"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference '{0}' is not installed for {1}. Searched: {2}.");

    /// <summary>
    /// An enabled framework reference name does not correspond to a known
    /// shared framework (it is absent from the known-framework allow-list).
    /// </summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceUnknownName = new(
        new DiagnosticId("MIST_FWREF_002"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference '{0}' is not a known shared framework.");

    // ── Package resolution diagnostics ───────────────────────────────────

    /// <summary>
    /// A declared package provides no assemblies compatible with the target framework.
    /// </summary>
    public static readonly DiagnosticDescriptor PackageNoCompatibleTfm = new(
        new DiagnosticId("MIST_PKG_001"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Package '{0}' version '{1}' does not provide assemblies compatible with the target framework '{2}'. Available frameworks: {3}.");

    /// <summary>
    /// A referenced NuGet package name or version could not be found on any configured NuGet feed.
    /// </summary>
    public static readonly DiagnosticDescriptor PackageNotFound = new(
        new DiagnosticId("MIST_PKG_002"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "{0}");

    /// <summary>
    /// A connection or network failure occurred while restoring a package, or the restore
    /// did not complete within the timeout bound.
    /// </summary>
    public static readonly DiagnosticDescriptor PackageNetworkError = new(
        new DiagnosticId("MIST_PKG_003"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "{0}");

    /// <summary>
    /// A CLR base type or implemented interface could not be resolved from any assembly
    /// available to the provider during projection. Only that relationship is omitted;
    /// the derived type and its remaining members continue to project.
    /// </summary>
    public static readonly DiagnosticDescriptor CrossAssemblyRelationshipUnresolved = new(
        new DiagnosticId("MIST_INTEROP_UNRESOLVED_RELATIONSHIP"),
        DiagnosticSeverity.Info,
        PipelineStage.Driver,
        "{0}");

    // ── Target-typed new diagnostics ─────────────────────────────────────

    /// <summary>
    /// A target-typed new expression is used in a <c>var</c> declaration.
    /// <c>var</c> does not provide a target type, so the type cannot be inferred.
    /// </summary>
    public static readonly DiagnosticDescriptor TargetNewWithVar = new(
        new DiagnosticId("MIST_TARGET_NEW_WITH_VAR"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The type of 'new()' cannot be inferred because 'var' does not provide a target type. Use an explicit type or provide the type name after 'new'.");

    /// <summary>
    /// A target-typed new expression appears in a context where no target type
    /// can be determined (e.g., bare expression statement).
    /// </summary>
    public static readonly DiagnosticDescriptor TargetNewNoContext = new(
        new DiagnosticId("MIST_TARGET_NEW_NO_CONTEXT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The type of 'new()' cannot be inferred from context.");

    /// <summary>
    /// A target-typed new expression resolves to an abstract type, which cannot
    /// be instantiated.
    /// </summary>
    public static readonly DiagnosticDescriptor TargetNewAbstract = new(
        new DiagnosticId("MIST_TARGET_NEW_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot create an instance of abstract type '{0}' with target-typed new.");

    // ── Void type diagnostics ────────────────────────────────────────────

    /// <summary>Void is not permitted as an array element type.</summary>
    public static readonly DiagnosticDescriptor VoidArrayNotPermitted = new(
        new DiagnosticId("MIST_TC_0034"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot create an array of type 'void'. Void is not permitted as an array element type.");

    // ── Default keyword diagnostics ──────────────────────────────────────

    /// <summary><c>default(T)</c> where T is not a defaultable type.</summary>
    public static readonly DiagnosticDescriptor DefaultNotSupported = new(
        new DiagnosticId("MIST_DEFAULT_NOT_SUPPORTED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not support the 'default' operator because it has non-defaultable fields without type bindings in a default block");

    /// <summary>More than one default block declared in a single type.</summary>
    public static readonly DiagnosticDescriptor DefaultDuplicateBlock = new(
        new DiagnosticId("MIST_DEFAULT_DUPLICATE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' declares more than one default block; only one is permitted per type.");

    /// <summary>Default block overrides a field that is not visible to the declaring type.</summary>
    public static readonly DiagnosticDescriptor DefaultFieldNotVisible = new(
        new DiagnosticId("MIST_DEFAULT_FIELD_NOT_VISIBLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' is not accessible from the default block of '{1}' because it is not public in the base type");

    /// <summary>Non-constant expression used in a default block context.</summary>
    public static readonly DiagnosticDescriptor DefaultNotConstant = new(
        new DiagnosticId("MIST_DEFAULT_NOT_CONSTANT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Default value for field '{0}' must be a compile-time constant expression");

    /// <summary><c>default(T)</c> where T cannot be resolved to a known type.</summary>
    public static readonly DiagnosticDescriptor DefaultUndefinedType = new(
        new DiagnosticId("MIST_DEFAULT_UNDEFINED_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The type in 'default(TypeName)' could not be resolved to a known type");

    /// <summary><c>default(T)</c> where T is an abstract type.</summary>
    public static readonly DiagnosticDescriptor DefaultOnAbstract = new(
        new DiagnosticId("MIST_DEFAULT_ON_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'default' operator is not supported on abstract type '{0}'. Only concrete types (classes and structs) support 'default'.");

    /// <summary><c>default(T)</c> where T is a type parameter.</summary>
    public static readonly DiagnosticDescriptor DefaultOnTypeParameter = new(
        new DiagnosticId("MIST_DEFAULT_ON_TYPE_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'default' operator is not supported on unconstrained generic type parameter '{0}'");

    /// <summary>Bare <c>default</c> used where the target type cannot be inferred.</summary>
    public static readonly DiagnosticDescriptor DefaultCannotInferType = new(
        new DiagnosticId("MIST_DEFAULT_CANNOT_INFER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The type of 'default' cannot be determined from context");

    /// <summary>Explicit default block produces the same values as the inferred default (informational).</summary>
    public static readonly DiagnosticDescriptor DefaultRedundantBlock = new(
        new DiagnosticId("MIST_DEFAULT_REDUNDANT"),
        DiagnosticSeverity.Info,
        PipelineStage.Analysis,
        "The explicit default block on type '{0}' is redundant and can be removed because it produces the same values as the inferred default");

    /// <summary>Abstract type field requires a type binding in the default block.</summary>
    public static readonly DiagnosticDescriptor DefaultRequiresTypeBinding = new(
        new DiagnosticId("MIST_DEFAULT_REQUIRES_BINDING"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Field '{0}' on type '{1}' has type '{2}' which is not defaultable. Either add a constructor to explicitly assign this field, or add a `default {{}}` block with a type binding.");

    /// <summary>Derived type's base class is not defaultable.</summary>
    public static readonly DiagnosticDescriptor DefaultBaseNotDefaultable = new(
        new DiagnosticId("MIST_DEFAULT_BASE_NOT_DEFAULTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' cannot be defaultable because its base class '{1}' is not defaultable.");

    /// <summary>More than one enum variant has the <c>default</c> modifier.</summary>
    public static readonly DiagnosticDescriptor EnumMultipleDefaults = new(
        new DiagnosticId("MIST_ENUM_MULTIPLE_DEFAULTS"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Enum '{0}' has multiple variants with the 'default' modifier. Only one variant may be designated as default.");

    // ── Definite assignment diagnostics ──────────────────────────────────

    /// <summary>A constructor body does not definitely assign a non-defaultable field.</summary>
    public static readonly DiagnosticDescriptor FieldNotDefinitelyAssigned = new(
        new DiagnosticId("MIST_FIELD_NOT_DEFINITELY_ASSIGNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' must be definitely assigned before constructor '{1}' completes. The field's type '{2}' is not defaultable and has no initializer.");

    /// <summary>A field is read before being definitely assigned in a constructor body.</summary>
    public static readonly DiagnosticDescriptor FieldReadBeforeAssignment = new(
        new DiagnosticId("MIST_FIELD_READ_BEFORE_ASSIGNMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' is read before being definitely assigned in constructor '{1}()'");

    /// <summary>A local variable is read before being definitely assigned.</summary>
    public static readonly DiagnosticDescriptor LocalReadBeforeAssignment = new(
        new DiagnosticId("MIST_LOCAL_READ_BEFORE_ASSIGNMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Local variable '{0}' is read before being definitely assigned");

    /// <summary>Bare <c>new T[size]</c> where T is not a stampable type; an initializer-based construction is required.</summary>
    public static readonly DiagnosticDescriptor ArrayRequiresInitializer = new(
        new DiagnosticId("MIST_ARRAY_REQUIRES_INITIALIZER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot create a bare array 'new {0}[size]' because {1}. Use 'new Array<{0}, Size>(i => ...)' to supply a value for each element, or use an array literal with explicit elements.");

    /// <summary>Type argument does not satisfy the <c>where T : default</c> constraint.</summary>
    public static readonly DiagnosticDescriptor DefaultConstraintNotSatisfied = new(
        new DiagnosticId("MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not satisfy the 'default' constraint on type parameter '{1}' of '{2}'.");

    // ── Associated type diagnostics ──────────────────────────────────────

    /// <summary>An abstract type declares two or more associated type slots with the same name.</summary>
    public static readonly DiagnosticDescriptor DuplicateAssociatedTypeSlot = new(
        new DiagnosticId("MIST_AT_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract type '{0}' declares duplicate associated type slot '{1}'");

    /// <summary>Implementor does not provide a concrete type for an associated type slot.</summary>
    public static readonly DiagnosticDescriptor MissingAssociatedTypeMember = new(
        new DiagnosticId("MIST_AT_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' does not provide a concrete type for associated type slot '{1}' required by '{2}'");

    /// <summary>Concrete type member does not satisfy a constraint of the associated type slot.</summary>
    public static readonly DiagnosticDescriptor UnsatisfiedAssociatedTypeConstraint = new(
        new DiagnosticId("MIST_AT_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type '{0}' does not satisfy constraint '{1}' on associated type slot '{2}' of '{3}'");

    /// <summary>Type parameter does not have an associated type with the given name.</summary>
    public static readonly DiagnosticDescriptor UnresolvedAssociatedTypeReference = new(
        new DiagnosticId("MIST_AT_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Type parameter '{0}' does not have an associated type '{1}'; '{0}' is not constrained by an abstract type declaring '{1}'.");

    /// <summary>Modifiers are not permitted on associated type slot declarations.</summary>
    public static readonly DiagnosticDescriptor ModifiersOnAssociatedTypeSlot = new(
        new DiagnosticId("MIST_AT_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Modifiers are not permitted on associated type slot declarations");

    /// <summary>Abstract type slots are only permitted in abstract types.</summary>
    public static readonly DiagnosticDescriptor AssociatedTypeSlotInWrongContext = new(
        new DiagnosticId("MIST_AT_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract type slots are only permitted in abstract types");
}