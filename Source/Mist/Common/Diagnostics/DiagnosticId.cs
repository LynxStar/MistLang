namespace Mist.Common.Diagnostics;

/// <summary>
/// A stable identifier for a compiler diagnostic.
/// </summary>
public readonly record struct DiagnosticId(string Value)
{
    // ── Generics diagnostics ─────────────────────────────────────────────

    /// <summary>Type argument count doesn't match type parameter count.</summary>
    public static readonly DiagnosticId ArityMismatch = new("MIST_ARITY_MISMATCH");

    /// <summary>Type arguments provided to a non-generic type.</summary>
    public static readonly DiagnosticId NotGeneric = new("MIST_NOT_GENERIC");

    /// <summary>Generic type used without type arguments.</summary>
    public static readonly DiagnosticId MissingTypeArguments = new("MIST_MISSING_TYPE_ARGUMENTS");

    /// <summary>Duplicate type parameter name in a type parameter list.</summary>
    public static readonly DiagnosticId DuplicateTypeParameter = new("MIST_DUPLICATE_TYPE_PARAMETER");

    /// <summary>Type parameter name shadows the enclosing type name.</summary>
    public static readonly DiagnosticId TypeParameterShadowsType = new("MIST_TYPE_PARAMETER_SHADOWS_TYPE");

    /// <summary>Method type argument count doesn't match method type parameter count.</summary>
    public static readonly DiagnosticId MethodArityMismatch = new("MIST_METHOD_ARITY_MISMATCH");

    /// <summary>Type argument cannot be resolved to a known type.</summary>
    public static readonly DiagnosticId UndefinedTypeArgument = new("MIST_UNDEFINED_TYPE_ARGUMENT");

    // ── Void type diagnostics ────────────────────────────────────────────

    /// <summary>Void is not permitted as an array element type.</summary>
    public static readonly DiagnosticId VoidArrayNotPermitted = new("MIST_TC_0034");

    // ── Standard Library loading diagnostics ─────────────────────────────

    /// <summary>Stdlib file missing, unreadable, or the stdlib directory is absent.</summary>
    public static readonly DiagnosticId StdlibParse = new("MIST_STDLIB_PARSE");

    /// <summary>
    /// The Standard Library has not been loaded (or a required stdlib type is
    /// absent from the loaded set) at a point in the pipeline that requires
    /// it. This is a fatal, compiler-internal condition: primitive keyword
    /// resolution (Requirement 7.6) and related stdlib-dependent machinery
    /// cannot proceed without the corresponding stdlib type. Raised by
    /// <c>SemanticModelBuilder.LookupQualifiedStdlibType</c> via
    /// <c>StdlibMissingException</c>.
    /// </summary>
    public static readonly DiagnosticId StdlibMissing = new("MIST_STDLIB_MISSING");

    // ── Backend diagnostics ──────────────────────────────────────────────

    /// <summary>
    /// An intrinsic member was invoked but the backend has no emission
    /// template registered for it. The compilation must fail with no output
    /// artifact (Requirements 5.9, 5.10).
    /// </summary>
    public static readonly DiagnosticId MissingIntrinsicEmissionTemplate = new("MIST_BE_0002");

    // ── Project reference diagnostics ────────────────────────────────────

    /// <summary>A [[references]] entry is missing the required 'path' field.</summary>
    public static readonly DiagnosticId ProjectReferenceMissingPath =
        new("MIST_PROJECT_REFERENCE_MISSING_PATH");

    /// <summary>A [[references]] entry has an empty or whitespace 'path' field.</summary>
    public static readonly DiagnosticId ProjectReferenceEmptyPath =
        new("MIST_PROJECT_REFERENCE_EMPTY_PATH");

    /// <summary>The normalized reference path does not point to an existing file or directory.</summary>
    public static readonly DiagnosticId ProjectReferenceNotFound =
        new("MIST_PROJECT_REFERENCE_NOT_FOUND");

    /// <summary>The normalized reference path points to a directory with no mist.toml.</summary>
    public static readonly DiagnosticId ProjectReferenceNoProjectFile =
        new("MIST_PROJECT_REFERENCE_NO_PROJECT_FILE");

    /// <summary>A circular reference was detected in the project reference graph.</summary>
    public static readonly DiagnosticId ProjectCircularReference =
        new("MIST_PROJECT_CIRCULAR_REFERENCE");

    /// <summary>A reference entry or the references field itself is not a valid TOML table.</summary>
    public static readonly DiagnosticId ProjectReferenceInvalidFormat =
        new("MIST_PROJECT_REFERENCE_INVALID_FORMAT");

    /// <summary>A reference key is empty, contains invalid characters, or exceeds 128 characters.</summary>
    public static readonly DiagnosticId ProjectReferenceInvalidName =
        new("MIST_PROJECT_REFERENCE_INVALID_NAME");

    /// <summary>The reference key does not match the target project's declared name.</summary>
    public static readonly DiagnosticId ProjectReferenceNameMismatch =
        new("MIST_PROJECT_REFERENCE_NAME_MISMATCH");

    /// <summary>The entry_point field is present but empty or whitespace.</summary>
    public static readonly DiagnosticId ProjectEmptyEntryPoint =
        new("MIST_PROJECT_EMPTY_ENTRY_POINT");

    // ── Default keyword diagnostics ──────────────────────────────────────

    /// <summary><c>default(T)</c> where T is not a defaultable type.</summary>
    public static readonly DiagnosticId DefaultNotSupported = new("MIST_DEFAULT_NOT_SUPPORTED");

    /// <summary>More than one default block declared in a single type.</summary>
    public static readonly DiagnosticId DefaultDuplicateBlock = new("MIST_DEFAULT_DUPLICATE");

    /// <summary>Default block overrides a field that is not visible to the declaring type.</summary>
    public static readonly DiagnosticId DefaultFieldNotVisible = new("MIST_DEFAULT_FIELD_NOT_VISIBLE");

    /// <summary>Non-constant expression used in a default block context.</summary>
    public static readonly DiagnosticId DefaultNotConstant = new("MIST_DEFAULT_NOT_CONSTANT");

    /// <summary><c>default(T)</c> where T cannot be resolved to a known type.</summary>
    public static readonly DiagnosticId DefaultUndefinedType = new("MIST_DEFAULT_UNDEFINED_TYPE");

    /// <summary><c>default(T)</c> where T is an interface type.</summary>
    public static readonly DiagnosticId DefaultOnInterface = new("MIST_DEFAULT_ON_INTERFACE");

    /// <summary><c>default(T)</c> where T is a trait type.</summary>
    public static readonly DiagnosticId DefaultOnTrait = new("MIST_DEFAULT_ON_TRAIT");

    /// <summary><c>default(T)</c> where T is a type parameter.</summary>
    public static readonly DiagnosticId DefaultOnTypeParameter = new("MIST_DEFAULT_ON_TYPE_PARAMETER");

    /// <summary>Bare <c>default</c> used where the target type cannot be inferred.</summary>
    public static readonly DiagnosticId DefaultCannotInferType = new("MIST_DEFAULT_CANNOT_INFER");

    /// <summary>Explicit default block produces the same values as the inferred default (informational).</summary>
    public static readonly DiagnosticId DefaultRedundantBlock = new("MIST_DEFAULT_REDUNDANT");

    /// <summary>Interface or trait field requires a type binding in the default block.</summary>
    public static readonly DiagnosticId DefaultRequiresTypeBinding = new("MIST_DEFAULT_REQUIRES_BINDING");

    /// <summary>Derived type's base class is not defaultable.</summary>
    public static readonly DiagnosticId DefaultBaseNotDefaultable = new("MIST_DEFAULT_BASE_NOT_DEFAULTABLE");

    /// <summary>More than one enum variant has the <c>default</c> modifier.</summary>
    public static readonly DiagnosticId EnumMultipleDefaults = new("MIST_ENUM_MULTIPLE_DEFAULTS");

    /// <summary>A constructor body does not definitely assign a non-defaultable field.</summary>
    public static readonly DiagnosticId FieldNotDefinitelyAssigned = new("MIST_FIELD_NOT_DEFINITELY_ASSIGNED");

    /// <summary>A field is read before being definitely assigned in a constructor body.</summary>
    public static readonly DiagnosticId FieldReadBeforeAssignment = new("MIST_FIELD_READ_BEFORE_ASSIGNMENT");

    /// <summary>A local variable is read before being definitely assigned.</summary>
    public static readonly DiagnosticId LocalReadBeforeAssignment = new("MIST_LOCAL_READ_BEFORE_ASSIGNMENT");

    /// <summary>Type argument does not satisfy the <c>where T : default</c> constraint.</summary>
    public static readonly DiagnosticId DefaultConstraintNotSatisfied = new("MIST_DEFAULT_CONSTRAINT_NOT_SATISFIED");

    // ── Trait inheritance diagnostics ────────────────────────────────────

    /// <summary>A parent reference in a trait's inheritance clause does not resolve to a trait.</summary>
    public static readonly DiagnosticId TraitParentNotTrait = new("MIST_TRAIT_PARENT_NOT_TRAIT");

    /// <summary>A trait lists the same parent trait more than once in its inheritance clause.</summary>
    public static readonly DiagnosticId TraitDuplicateParent = new("MIST_TRAIT_DUPLICATE_PARENT");

    /// <summary>A trait inheritance chain contains a cycle.</summary>
    public static readonly DiagnosticId TraitInheritanceCycle = new("MIST_TRAIT_INHERITANCE_CYCLE");

    // ── Entry point diagnostics ──────────────────────────────────────────

    /// <summary>Multiple files contain top-level statements in the same project.</summary>
    public static readonly DiagnosticId MultipleTopLevelStatementFiles = new("MIST_EP_0001");

    /// <summary>TLS + Main conflict without <c>entry_point</c> config in mist.toml.</summary>
    public static readonly DiagnosticId TlsAndMainWithoutConfig = new("MIST_EP_0002");

    /// <summary>TLS exists but <c>entry_point</c> specifies a class name (TLS must be entry point).</summary>
    public static readonly DiagnosticId TlsWithClassEntryPoint = new("MIST_EP_0003");

    /// <summary>Multiple classes contain <c>static void Main</c> without <c>entry_point</c> config.</summary>
    public static readonly DiagnosticId MultipleMainsWithoutConfig = new("MIST_EP_0004");

    /// <summary><c>entry_point = "top-level"</c> specified but no file contains top-level statements.</summary>
    public static readonly DiagnosticId TopLevelEntryPointButNoTls = new("MIST_EP_0005");

    /// <summary>A TLS file contains a block namespace declaration (only file-scoped namespaces are allowed).</summary>
    public static readonly DiagnosticId TlsWithBlockNamespace = new("MIST_EP_0006");

    /// <summary><c>entry_point</c> specifies a class that doesn't exist or lacks a <c>static void Main</c>.</summary>
    public static readonly DiagnosticId EntryPointClassNotFound = new("MIST_EP_0007");

    // ── Associated type diagnostics ──────────────────────────────────────

    /// <summary>An interface or trait declares two or more associated type slots with the same name.</summary>
    public static readonly DiagnosticId DuplicateAssociatedTypeSlot = new("MIST_AT_0001");

    /// <summary>Implementor does not provide a concrete type for an associated type slot.</summary>
    public static readonly DiagnosticId MissingAssociatedTypeMember = new("MIST_AT_0002");

    /// <summary>Concrete type member does not satisfy a constraint of the associated type slot.</summary>
    public static readonly DiagnosticId UnsatisfiedAssociatedTypeConstraint = new("MIST_AT_0003");

    /// <summary>Type parameter does not have an associated type with the given name.</summary>
    public static readonly DiagnosticId UnresolvedAssociatedTypeReference = new("MIST_AT_0004");

    /// <summary>Modifiers are not permitted on associated type slot declarations.</summary>
    public static readonly DiagnosticId ModifiersOnAssociatedTypeSlot = new("MIST_AT_0005");

    /// <summary>Abstract type slots are only permitted in interfaces and traits.</summary>
    public static readonly DiagnosticId AssociatedTypeSlotInWrongContext = new("MIST_AT_0006");

    // ── Assembly Type Provider diagnostics ───────────────────────────────

    /// <summary>The .NET SDK installation could not be located.</summary>
    public static readonly DiagnosticId SdkNotFound = new("MIST_SDK_NOT_FOUND");

    /// <summary>The target framework reference path does not exist in the SDK.</summary>
    public static readonly DiagnosticId SdkNoFramework = new("MIST_SDK_NO_FRAMEWORK");

    /// <summary>A referenced NuGet package could not be found on any configured feed.</summary>
    public static readonly DiagnosticId PackageNotFound = new("MIST_PKG_NOT_FOUND");

    /// <summary>A network error or timeout occurred during package download.</summary>
    public static readonly DiagnosticId PackageNetworkError = new("MIST_PKG_NETWORK_ERROR");

    /// <summary>A NuGet package has no compatible framework target.</summary>
    public static readonly DiagnosticId PackageNoCompatibleTfm = new("MIST_PKG_NO_COMPATIBLE_TFM");

    /// <summary>A <c>using</c> directive references a namespace that does not exist.</summary>
    public static readonly DiagnosticId NamespaceNotFound = new("MIST_NS_NOT_FOUND");

    /// <summary>A type name is ambiguous between multiple imported CLR namespaces.</summary>
    public static readonly DiagnosticId TypeAmbiguous = new("MIST_TYPE_AMBIGUOUS");
}
