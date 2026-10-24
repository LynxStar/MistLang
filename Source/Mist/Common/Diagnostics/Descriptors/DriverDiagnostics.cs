#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the compiler driver, project file parsing,
/// and related infrastructure (loading, package/framework resolution, csproj properties).
/// </summary>
public static class DriverDiagnostics
{
    // ── Project file diagnostics ─────────────────────────────────────────

    /// <summary>The mist.toml project file does not exist at the expected path.</summary>
    public static readonly DiagnosticDescriptor ProjectFileNotFound = new(
        new DiagnosticId("MIST_PROJECT_FILE_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Project file '{0}' not found.");

    /// <summary>An I/O error occurred while reading the mist.toml project file.</summary>
    public static readonly DiagnosticDescriptor ProjectFileReadError = new(
        new DiagnosticId("MIST_PROJECT_FILE_READ_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to read project file '{0}': {1}");

    /// <summary>The mist.toml file contains invalid TOML syntax.</summary>
    public static readonly DiagnosticDescriptor ProjectTomlError = new(
        new DiagnosticId("MIST_PROJECT_TOML_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to parse project file '{0}': {1}");

    /// <summary>The mist.toml file is missing the required 'name' field.</summary>
    public static readonly DiagnosticDescriptor ProjectMissingName = new(
        new DiagnosticId("MIST_PROJECT_MISSING_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Project file '{0}' is missing the required 'name' field.");

    /// <summary>The 'name' field in mist.toml is empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor ProjectEmptyName = new(
        new DiagnosticId("MIST_PROJECT_EMPTY_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Project name in '{0}' must not be empty or whitespace.");

    /// <summary>The 'name' field contains characters outside the allowed set.</summary>
    public static readonly DiagnosticDescriptor ProjectInvalidName = new(
        new DiagnosticId("MIST_PROJECT_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Project name in '{0}' contains invalid characters: {1}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.");

    /// <summary>The 'name' field exceeds the maximum allowed length.</summary>
    public static readonly DiagnosticDescriptor ProjectNameTooLong = new(
        new DiagnosticId("MIST_PROJECT_NAME_TOO_LONG"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Project name in '{0}' exceeds the maximum length of {1} characters (got {2}).");

    /// <summary>The 'namespace_root' field is present but empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor ProjectEmptyNamespaceRoot = new(
        new DiagnosticId("MIST_PROJECT_EMPTY_NAMESPACE_ROOT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Namespace root in '{0}' must not be empty or whitespace when specified.");

    /// <summary>The 'namespace_root' field contains characters outside the allowed set.</summary>
    public static readonly DiagnosticDescriptor ProjectInvalidNamespaceRoot = new(
        new DiagnosticId("MIST_PROJECT_INVALID_NAMESPACE_ROOT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Namespace root in '{0}' contains invalid characters: {1}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.");

    /// <summary>The 'entry_point' field is present but empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor ProjectEmptyEntryPoint = new(
        new DiagnosticId("MIST_PROJECT_EMPTY_ENTRY_POINT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Entry point in '{0}' must not be empty or whitespace when specified.");

    /// <summary>The 'output_type' field has an invalid value (not "executable" or "library").</summary>
    public static readonly DiagnosticDescriptor ProjectInvalidOutputType = new(
        new DiagnosticId("MIST_PROJECT_INVALID_OUTPUT_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Invalid output_type '{0}' in '{1}'. Valid values are \"executable\" or \"library\".");

    // ── Project reference diagnostics ────────────────────────────────────

    /// <summary>A [[references]] entry is missing the required 'path' field.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceMissingPath = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_MISSING_PATH"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference '{0}' in '{1}' is missing the required 'path' field.");

    /// <summary>A [[references]] entry has an empty or whitespace 'path' field.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceEmptyPath = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_EMPTY_PATH"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference '{0}' in '{1}' has an empty or whitespace 'path' field.");

    /// <summary>The normalized reference path does not point to an existing file or directory.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceNotFound = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Referenced project path '{0}' does not exist.");

    /// <summary>The normalized reference path points to a directory with no mist.toml.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceNoProjectFile = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_NO_PROJECT_FILE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Referenced directory '{0}' holds no mist.toml, .csproj or .dll.");

    /// <summary>A circular reference was detected in the project reference graph.</summary>
    public static readonly DiagnosticDescriptor ProjectCircularReference = new(
        new DiagnosticId("MIST_PROJECT_CIRCULAR_REFERENCE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Circular project reference detected: {0}.");

    /// <summary>A reference entry or the references field itself is not a valid TOML table.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceInvalidFormat = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_INVALID_FORMAT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference '{0}' in '{1}' must be a table (e.g., {0} = {{ path = \"...\" }}).");

    /// <summary>The [references] field is present but is not a TOML table.</summary>
    public static readonly DiagnosticDescriptor ProjectReferencesNotTable = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_INVALID_FORMAT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The 'references' field in '{0}' must be a table ([references]).");

    /// <summary>A reference key is empty, contains invalid characters, or exceeds 128 characters.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceInvalidName = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference key '{0}' in '{1}' contains invalid characters. Only alphanumeric characters, hyphens, underscores, and periods are allowed.");

    /// <summary>A reference key is empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceEmptyName = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference key '{0}' in '{1}' must not be empty or whitespace.");

    /// <summary>A reference key exceeds the maximum allowed length.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceNameTooLong = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference key '{0}' in '{1}' exceeds the maximum length of {2} characters (got {3}).");

    /// <summary>The reference key does not match the target project's declared name.</summary>
    public static readonly DiagnosticDescriptor ProjectReferenceNameMismatch = new(
        new DiagnosticId("MIST_PROJECT_REFERENCE_NAME_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference key '{0}' does not match '{1}', the name the {2} '{3}' produces.");

    public static readonly DiagnosticDescriptor ReferenceUnsupportedPath = new(
        new DiagnosticId("MIST_REFERENCE_UNSUPPORTED_PATH"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference '{0}' names '{1}', which is not a mist.toml, a .csproj or a .dll.");

    public static readonly DiagnosticDescriptor ReferenceFileNotFound = new(
        new DiagnosticId("MIST_REFERENCE_FILE_NOT_FOUND"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference '{0}' names '{1}', which does not exist.");

    public static readonly DiagnosticDescriptor ReferenceAmbiguous = new(
        new DiagnosticId("MIST_REFERENCE_AMBIGUOUS"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference '{0}' names directory '{1}', which holds more than one referenceable file: {2}. Name the intended file in 'path'.");

    public static readonly DiagnosticDescriptor CSharpProjectBuildFailed = new(
        new DiagnosticId("MIST_CSHARP_PROJECT_BUILD_FAILED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Building C# project '{0}' for reference '{1}' failed: {2}");

    public static readonly DiagnosticDescriptor ReferenceCycle = new(
        new DiagnosticId("MIST_REFERENCE_CYCLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Reference cycle: {0}.");

    // ── Global using diagnostics ─────────────────────────────────────────

    /// <summary>The [global_usings] section is not a TOML table.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingInvalidFormat = new(
        new DiagnosticId("MIST_GLOBAL_USING_INVALID_FORMAT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The '[global_usings]' section in '{0}' must be a table.");

    /// <summary>The 'namespaces' field in [global_usings] is not an array.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingNamespacesNotArray = new(
        new DiagnosticId("MIST_GLOBAL_USING_INVALID_FORMAT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The 'namespaces' field in '[global_usings]' section of '{0}' must be an array.");

    /// <summary>An element in the global_usings namespaces array is not a string.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingElementNotString = new(
        new DiagnosticId("MIST_GLOBAL_USING_INVALID_FORMAT"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Element at index {0} in 'namespaces' array of '{1}' must be a string.");

    /// <summary>The [global_usings] table is missing the required 'namespaces' field.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingMissingNamespaces = new(
        new DiagnosticId("MIST_GLOBAL_USING_MISSING_NAMESPACES"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The '[global_usings]' section in '{0}' is missing the required 'namespaces' field.");

    /// <summary>A namespace entry in the global_usings array is empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingEmptyNamespace = new(
        new DiagnosticId("MIST_GLOBAL_USING_EMPTY_NAMESPACE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Namespace entry at index {0} in '{1}' must not be empty or whitespace.");

    /// <summary>A namespace entry contains characters not valid for a namespace identifier.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingInvalidNamespace = new(
        new DiagnosticId("MIST_GLOBAL_USING_INVALID_NAMESPACE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Namespace entry '{0}' in '{1}' contains invalid characters: {2}.");

    /// <summary>A namespace entry appears more than once in the global_usings array.</summary>
    public static readonly DiagnosticDescriptor GlobalUsingDuplicateNamespace = new(
        new DiagnosticId("MIST_GLOBAL_USING_DUPLICATE_NAMESPACE"),
        DiagnosticSeverity.Warning,
        PipelineStage.Driver,
        "Duplicate namespace '{0}' in '{1}' will be ignored.");

    // ── Package diagnostics ──────────────────────────────────────────────

    /// <summary>The [packages] field is present but is not a TOML table.</summary>
    public static readonly DiagnosticDescriptor PackageTableExpected = new(
        new DiagnosticId("MIST_PKG_TABLE_EXPECTED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The 'packages' field in '{0}' must be a table ([packages]).");

    /// <summary>A package name contains invalid characters or exceeds the maximum length.</summary>
    public static readonly DiagnosticDescriptor PackageInvalidName = new(
        new DiagnosticId("MIST_PKG_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Package name '{0}' in '{1}' contains invalid characters: {2}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.");

    /// <summary>A package name exceeds the maximum allowed length.</summary>
    public static readonly DiagnosticDescriptor PackageNameTooLong = new(
        new DiagnosticId("MIST_PKG_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Package name '{0}' in '{1}' exceeds the maximum length of {2} characters (got {3}).");

    /// <summary>A package version value is not a string or is empty/whitespace.</summary>
    public static readonly DiagnosticDescriptor PackageInvalidVersion = new(
        new DiagnosticId("MIST_PKG_INVALID_VERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Package '{0}' in '{1}' must have a string version value.");

    /// <summary>A package version value is empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor PackageEmptyVersion = new(
        new DiagnosticId("MIST_PKG_INVALID_VERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Package '{0}' in '{1}' has an empty or whitespace version string.");

    // ── Framework reference diagnostics ──────────────────────────────────

    /// <summary>The [framework_references] field is present but is not a TOML table.</summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceTableExpected = new(
        new DiagnosticId("MIST_FWREF_TABLE_EXPECTED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The 'framework_references' field in '{0}' must be a table ([framework_references]).");

    /// <summary>A framework reference name is empty/whitespace after trimming.</summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceInvalidName = new(
        new DiagnosticId("MIST_FWREF_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference name '{0}' in '{1}' must not be empty or whitespace after trimming.");

    /// <summary>A framework reference name exceeds the maximum allowed length.</summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceNameTooLong = new(
        new DiagnosticId("MIST_FWREF_INVALID_NAME"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference name '{0}' in '{1}' exceeds the maximum length of {2} characters (got {3}).");

    /// <summary>A framework reference value is not a boolean.</summary>
    public static readonly DiagnosticDescriptor FrameworkReferenceInvalidValue = new(
        new DiagnosticId("MIST_FWREF_INVALID_VALUE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Framework reference '{0}' in '{1}' must have a boolean value (true or false).");

    // ── Csproj property diagnostics ──────────────────────────────────────

    /// <summary>The [csproj_properties] field is present but is not a TOML table.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyTableExpected = new(
        new DiagnosticId("MIST_CSPROJ_PROP_TABLE_EXPECTED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "The 'csproj_properties' field in '{0}' must be a table ([csproj_properties]).");

    /// <summary>A property key in [csproj_properties] is empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyEmptyKey = new(
        new DiagnosticId("MIST_CSPROJ_PROP_EMPTY_KEY"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Property key in '[csproj_properties]' of '{0}' must not be empty or whitespace.");

    /// <summary>A property key in [csproj_properties] contains invalid characters.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyInvalidKey = new(
        new DiagnosticId("MIST_CSPROJ_PROP_INVALID_KEY"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Property key '{0}' in '[csproj_properties]' of '{1}' contains invalid characters: {2}.");

    /// <summary>A property key in [csproj_properties] exceeds the maximum allowed length.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyKeyTooLong = new(
        new DiagnosticId("MIST_CSPROJ_PROP_KEY_TOO_LONG"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Property key '{0}' in '[csproj_properties]' of '{1}' exceeds the maximum length of {2} characters (got {3}).");

    /// <summary>A property value in [csproj_properties] is not a string.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyInvalidValue = new(
        new DiagnosticId("MIST_CSPROJ_PROP_INVALID_VALUE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Property '{0}' in '[csproj_properties]' of '{1}' must have a string value.");

    /// <summary>A property value in [csproj_properties] is empty or whitespace.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyEmptyValue = new(
        new DiagnosticId("MIST_CSPROJ_PROP_EMPTY_VALUE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Property '{0}' in '[csproj_properties]' of '{1}' has an empty or whitespace value.");

    /// <summary>A property key in [csproj_properties] overrides a compiler-managed property.</summary>
    public static readonly DiagnosticDescriptor CsprojPropertyManagedOverride = new(
        new DiagnosticId("MIST_CSPROJ_PROP_MANAGED_OVERRIDE"),
        DiagnosticSeverity.Warning,
        PipelineStage.Driver,
        "Property '{0}' in '[csproj_properties]' of '{1}' overrides a compiler-managed property. This may cause unexpected build behavior.");

    // ── Compiler driver diagnostics ──────────────────────────────────────

    /// <summary>The requested backend identifier does not match any registered backend.</summary>
    public static readonly DiagnosticDescriptor UnknownBackend = new(
        new DiagnosticId("MIST_UNKNOWN_BACKEND"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Unknown backend '{0}'.");

    public static readonly DiagnosticDescriptor RuntimeIdentifierUnknown = new(
        new DiagnosticId("MIST_RID_UNKNOWN"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Runtime identifier '{0}' is not in the SDK's runtime identifier graph.");

    /// <summary>A source file or referenced source file could not be read due to an I/O error.</summary>
    public static readonly DiagnosticDescriptor FileError = new(
        new DiagnosticId("MIST_FILE_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to read source file '{0}': {1}");
}