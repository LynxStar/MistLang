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

    public static readonly MistDiagnostic ProjectFileNotFound = new(
        DiagnosticNumber.Mint("MIST0049"),
        Title: "Project file not found",
        Template: "Project file '{0}' not found.",
        Description: "No mist.toml exists at the path the compiler was given or derived from the working directory. Run the compiler from the project folder, or pass the path of an existing mist.toml.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectFileReadError = new(
        DiagnosticNumber.Mint("MIST0050"),
        Title: "Project file unreadable",
        Template: "Failed to read project file '{0}': {1}",
        Description: "The mist.toml exists but the operating system refused to read it; the trailing message is the I/O error. Check the file's permissions and that no other process holds it locked, then retry.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectTomlError = new(
        DiagnosticNumber.Mint("MIST0051"),
        Title: "Project file is not valid TOML",
        Template: "Failed to parse project file '{0}': {1}",
        Description: "The contents of mist.toml could not be parsed as TOML; the trailing message comes from the TOML parser and names the offending position. Correct the syntax at that position, typically a missing quote, bracket or equals sign.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectMissingName = new(
        DiagnosticNumber.Mint("MIST0052"),
        Title: "Project name missing",
        Template: "Project file '{0}' is missing the required 'name' field.",
        Description: "Every project must declare a name, and this mist.toml has no top-level 'name' key. Add one, for example name = \"MyApp\"; the name becomes the output assembly's name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectEmptyName = new(
        DiagnosticNumber.Mint("MIST0053"),
        Title: "Project name empty",
        Template: "Project name in '{0}' must not be empty or whitespace.",
        Description: "The 'name' key is present in mist.toml but holds nothing but whitespace, so there is no assembly name to build with. Give it a real value.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectInvalidName = new(
        DiagnosticNumber.Mint("MIST0054"),
        Title: "Project name has invalid characters",
        Template: "Project name in '{0}' contains invalid characters: {1}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
        Description: "The 'name' value in mist.toml uses characters outside the set permitted for an assembly file name. Remove or replace the listed characters so the name is made of letters, digits, hyphens, underscores and periods.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectNameTooLong = new(
        DiagnosticNumber.Mint("MIST0055"),
        Title: "Project name too long",
        Template: "Project name in '{0}' exceeds the maximum length of {1} characters (got {2}).",
        Description: "The 'name' value in mist.toml is longer than the compiler accepts for an assembly name. Shorten it to fit within the stated limit.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectEmptyNamespaceRoot = new(
        DiagnosticNumber.Mint("MIST0056"),
        Title: "Namespace root empty",
        Template: "Namespace root in '{0}' must not be empty or whitespace when specified.",
        Description: "The optional 'namespace_root' key is present in mist.toml but holds only whitespace. Either give it a namespace such as MyCompany.MyApp or remove the key so the project name is used as the root.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectInvalidNamespaceRoot = new(
        DiagnosticNumber.Mint("MIST0057"),
        Title: "Namespace root has invalid characters",
        Template: "Namespace root in '{0}' contains invalid characters: {1}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
        Description: "The 'namespace_root' value in mist.toml cannot form a valid namespace because of the listed characters. Replace them so the value is a sequence of identifiers separated by periods.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectEmptyEntryPoint = new(
        DiagnosticNumber.Mint("MIST0058"),
        Title: "Entry point setting empty",
        Template: "Entry point in '{0}' must not be empty or whitespace when specified.",
        Description: "The optional 'entry_point' key is present in mist.toml but holds only whitespace. Set it to a class name or to \"top-level\", or delete the key to let the compiler discover the entry point.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectInvalidOutputType = new(
        DiagnosticNumber.Mint("MIST0059"),
        Title: "Unrecognized output type",
        Template: "Invalid output_type '{0}' in '{1}'. Valid values are \"executable\" or \"library\".",
        Description: "The 'output_type' key in mist.toml holds a value the compiler does not know. Choose executable for a program with an entry point or library for a reusable assembly, or remove the key to accept the default.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Project reference diagnostics ────────────────────────────────────

    public static readonly MistDiagnostic ProjectReferenceMissingPath = new(
        DiagnosticNumber.Mint("MIST0060"),
        Title: "Reference has no path",
        Template: "Reference '{0}' in '{1}' is missing the required 'path' field.",
        Description: "An entry under [references] in mist.toml does not say what it points at, and 'path' is the only way to name the target. Add a path value giving the directory, mist.toml, .csproj or .dll of the referenced project.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectReferenceEmptyPath = new(
        DiagnosticNumber.Mint("MIST0061"),
        Title: "Reference path empty",
        Template: "Reference '{0}' in '{1}' has an empty or whitespace 'path' field.",
        Description: "An entry under [references] in mist.toml has a 'path' key whose value is only whitespace. Fill it in with the location of the referenced project, relative to the referencing mist.toml.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectReferenceNotFound = new(
        DiagnosticNumber.Mint("MIST0062"),
        Title: "Referenced project path missing",
        Template: "Referenced project path '{0}' does not exist.",
        Description: "The 'path' of a [references] entry was resolved relative to the project file, but nothing is on disk at that location. Check the spelling and relative position of the path, and make sure the referenced project is checked out.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectReferenceNoProjectFile = new(
        DiagnosticNumber.Mint("MIST0063"),
        Title: "Referenced directory has no project",
        Template: "Referenced directory '{0}' holds no mist.toml, .csproj or .dll.",
        Description: "A reference points at a directory that exists but contains none of the files the compiler can consume as a dependency. Point the path at the project's own directory, or name the project file or assembly directly.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectCircularReference = new(
        DiagnosticNumber.Mint("MIST0064"),
        Title: "Circular project reference",
        Template: "Circular project reference detected: {0}.",
        Description: "Following [references] from this Mist project leads back to itself along the chain shown, so no build order exists. Break the cycle by removing one of the references or moving the shared code into a third project.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectReferenceInvalidFormat = new(
        DiagnosticNumber.Mint("MIST0065"),
        Title: "Reference entry not a table",
        Template: "Reference '{0}' in '{1}' must be a table (e.g., {0} = {{ path = \"...\" }}).",
        Description: "Each key under [references] in mist.toml must map to an inline or sub-table carrying at least a 'path', but this one maps to a scalar or an array. Rewrite the entry as a table.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0066")]);

    public static readonly MistDiagnostic ProjectReferencesNotTable = new(
        DiagnosticNumber.Mint("MIST0066"),
        Title: "References section not a table",
        Template: "The 'references' field in '{0}' must be a table ([references]).",
        Description: "The 'references' key in mist.toml holds a scalar or an array where a table is required. Declare it as a table whose keys are project names and whose values are tables with a 'path'.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0065")]);

    public static readonly MistDiagnostic ProjectReferenceInvalidName = new(
        DiagnosticNumber.Mint("MIST0067"),
        Title: "Reference key has invalid characters",
        Template: "Reference key '{0}' in '{1}' contains invalid characters. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
        Description: "A key under [references] in mist.toml uses characters outside the set permitted for a project name. Since the key must equal the referenced project's name, rename it to match that name exactly.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0068"), DiagnosticNumber.Mint("MIST0069")]);

    public static readonly MistDiagnostic ProjectReferenceEmptyName = new(
        DiagnosticNumber.Mint("MIST0068"),
        Title: "Reference key empty",
        Template: "Reference key '{0}' in '{1}' must not be empty or whitespace.",
        Description: "A key under [references] in mist.toml is blank. Each key must be the name of the project it references; give it that name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0067"), DiagnosticNumber.Mint("MIST0069")]);

    public static readonly MistDiagnostic ProjectReferenceNameTooLong = new(
        DiagnosticNumber.Mint("MIST0069"),
        Title: "Reference key too long",
        Template: "Reference key '{0}' in '{1}' exceeds the maximum length of {2} characters (got {3}).",
        Description: "A key under [references] in mist.toml is longer than the compiler accepts for a project name. Shorten it; it must still equal the referenced project's name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0067"), DiagnosticNumber.Mint("MIST0068")]);

    public static readonly MistDiagnostic ProjectReferenceNameMismatch = new(
        DiagnosticNumber.Mint("MIST0070"),
        Title: "Reference key mismatch",
        Template: "Reference key '{0}' does not match '{1}', the name the {2} '{3}' produces.",
        Description: "The key chosen for a [references] entry differs from the name the referenced mist.toml, .csproj or .dll actually produces, and the compiler uses the key to identify the dependency. Rename the key to the name shown, or point the path at the intended project.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReferenceUnsupportedPath = new(
        DiagnosticNumber.Mint("MIST0071"),
        Title: "Unsupported reference file kind",
        Template: "Reference '{0}' names '{1}', which is not a mist.toml, a .csproj or a .dll.",
        Description: "A [references] entry names a file of a kind the compiler cannot consume; only a Mist project, a C# project or a compiled assembly can be referenced. Change the path to the project's mist.toml or .csproj, or to the .dll it produces.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReferenceFileNotFound = new(
        DiagnosticNumber.Mint("MIST0072"),
        Title: "Referenced file not found",
        Template: "Reference '{0}' names '{1}', which does not exist.",
        Description: "A [references] entry names a specific file, but once the path is resolved against the referencing mist.toml no such file is on disk. Fix the path, or build the referenced project so the file exists.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReferenceAmbiguous = new(
        DiagnosticNumber.Mint("MIST0073"),
        Title: "Reference directory ambiguous",
        Template: "Reference '{0}' names directory '{1}', which holds more than one referenceable file: {2}. Name the intended file in 'path'.",
        Description: "A [references] entry points at a directory holding several files the compiler could treat as the dependency, so it cannot pick one. Extend the path to the specific mist.toml, .csproj or .dll that is meant.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CSharpProjectBuildFailed = new(
        DiagnosticNumber.Mint("MIST0074"),
        Title: "C# project build failed",
        Template: "Building C# project '{0}' for reference '{1}' failed: {2}",
        Description: "A referenced .csproj had to be built to obtain its assembly, and the dotnet build did not succeed; the trailing text is the build output. Fix the errors in that C# project, or reference its already-built .dll instead.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReferenceCycle = new(
        DiagnosticNumber.Mint("MIST0075"),
        Title: "Reference cycle",
        Template: "Reference cycle: {0}.",
        Description: "The graph of Mist, C# and assembly references contains a loop, shown as the chain of names, so the projects cannot be built in order. Remove one of the references in the chain.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Global using diagnostics ─────────────────────────────────────────

    public static readonly MistDiagnostic GlobalUsingInvalidFormat = new(
        DiagnosticNumber.Mint("MIST0076"),
        Title: "Global usings section not a table",
        Template: "The '[global_usings]' section in '{0}' must be a table.",
        Description: "The 'global_usings' key in mist.toml holds a scalar or an array where a table is required. Declare it as a table with a 'namespaces' array inside.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0077"), DiagnosticNumber.Mint("MIST0078")]);

    public static readonly MistDiagnostic GlobalUsingNamespacesNotArray = new(
        DiagnosticNumber.Mint("MIST0077"),
        Title: "Global usings namespaces not array",
        Template: "The 'namespaces' field in '[global_usings]' section of '{0}' must be an array.",
        Description: "The 'namespaces' key under [global_usings] in mist.toml holds some value other than a list of namespace names. Write it as namespaces = [\"System\", \"System.Linq\"].",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0076"), DiagnosticNumber.Mint("MIST0078")]);

    public static readonly MistDiagnostic GlobalUsingElementNotString = new(
        DiagnosticNumber.Mint("MIST0078"),
        Title: "Global using entry not string",
        Template: "Element at index {0} in 'namespaces' array of '{1}' must be a string.",
        Description: "An element of the 'namespaces' array under [global_usings] in mist.toml is a number, boolean, table or nested array rather than text. Replace it with the namespace name in quotes.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0076"), DiagnosticNumber.Mint("MIST0077")]);

    public static readonly MistDiagnostic GlobalUsingMissingNamespaces = new(
        DiagnosticNumber.Mint("MIST0079"),
        Title: "Global usings missing namespaces key",
        Template: "The '[global_usings]' section in '{0}' is missing the required 'namespaces' field.",
        Description: "A [global_usings] table exists in mist.toml but has no 'namespaces' array, so it declares nothing. Add the array listing the namespaces to import everywhere, or remove the empty table.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic GlobalUsingEmptyNamespace = new(
        DiagnosticNumber.Mint("MIST0080"),
        Title: "Global using entry empty",
        Template: "Namespace entry at index {0} in '{1}' must not be empty or whitespace.",
        Description: "An element of the 'namespaces' array under [global_usings] in mist.toml is a blank string. Remove it or replace it with a namespace name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic GlobalUsingInvalidNamespace = new(
        DiagnosticNumber.Mint("MIST0081"),
        Title: "Global using has invalid characters",
        Template: "Namespace entry '{0}' in '{1}' contains invalid characters: {2}.",
        Description: "An element of the 'namespaces' array under [global_usings] in mist.toml is not a well-formed namespace name because of the listed characters. Correct it to dotted identifiers such as System.Collections.Generic.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic GlobalUsingDuplicateNamespace = new(
        DiagnosticNumber.Mint("MIST0082"),
        Title: "Duplicate global using",
        Template: "Duplicate namespace '{0}' in '{1}' will be ignored.",
        Description: "The same namespace appears more than once in the 'namespaces' array under [global_usings] in mist.toml, and only the first occurrence has any effect. Remove the repeated entry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning);

    // ── Package diagnostics ──────────────────────────────────────────────

    public static readonly MistDiagnostic PackageTableExpected = new(
        DiagnosticNumber.Mint("MIST0083"),
        Title: "Packages section not a table",
        Template: "The 'packages' field in '{0}' must be a table ([packages]).",
        Description: "The 'packages' key in mist.toml holds a scalar or an array where a table is required. Declare it as a table whose keys are NuGet package ids and whose values are version strings.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PackageInvalidName = new(
        DiagnosticNumber.Mint("MIST0084"),
        Title: "Package name has invalid characters",
        Template: "Package name '{0}' in '{1}' contains invalid characters: {2}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
        Description: "A key under [packages] in mist.toml uses characters that cannot occur in a NuGet package id. Correct the key to the package's exact id as published on the feed.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0085")]);

    public static readonly MistDiagnostic PackageNameTooLong = new(
        DiagnosticNumber.Mint("MIST0085"),
        Title: "Package name too long",
        Template: "Package name '{0}' in '{1}' exceeds the maximum length of {2} characters (got {3}).",
        Description: "A key under [packages] in mist.toml is longer than any NuGet package id the compiler accepts. Shorten it to the real package id, which fits within the stated limit.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0084")]);

    public static readonly MistDiagnostic PackageInvalidVersion = new(
        DiagnosticNumber.Mint("MIST0086"),
        Title: "Package version not a string",
        Template: "Package '{0}' in '{1}' must have a string version value.",
        Description: "The value of a [packages] entry in mist.toml is a bare number, boolean or table where a quoted version is required. Quote the version, for example Newtonsoft.Json = \"13.0.3\".",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0087")]);

    public static readonly MistDiagnostic PackageEmptyVersion = new(
        DiagnosticNumber.Mint("MIST0087"),
        Title: "Package version empty",
        Template: "Package '{0}' in '{1}' has an empty or whitespace version string.",
        Description: "The value of a [packages] entry in mist.toml is a blank string, so there is no version to restore. Fill in the version the project should use.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0086")]);

    // ── Framework reference diagnostics ──────────────────────────────────

    public static readonly MistDiagnostic FrameworkReferenceTableExpected = new(
        DiagnosticNumber.Mint("MIST0088"),
        Title: "Framework references not a table",
        Template: "The 'framework_references' field in '{0}' must be a table ([framework_references]).",
        Description: "The 'framework_references' key in mist.toml holds a scalar or an array where a table is required. Declare it as a table mapping shared framework names to true or false.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FrameworkReferenceInvalidName = new(
        DiagnosticNumber.Mint("MIST0089"),
        Title: "Framework reference name empty",
        Template: "Framework reference name '{0}' in '{1}' must not be empty or whitespace after trimming.",
        Description: "A key under [framework_references] in mist.toml is blank once surrounding whitespace is removed. Replace it with the name of a shared framework such as Microsoft.AspNetCore.App.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0090")]);

    public static readonly MistDiagnostic FrameworkReferenceNameTooLong = new(
        DiagnosticNumber.Mint("MIST0090"),
        Title: "Framework reference name too long",
        Template: "Framework reference name '{0}' in '{1}' exceeds the maximum length of {2} characters (got {3}).",
        Description: "A key under [framework_references] in mist.toml is longer than the compiler accepts for a framework name. Shorten it to the real shared framework name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0089")]);

    public static readonly MistDiagnostic FrameworkReferenceInvalidValue = new(
        DiagnosticNumber.Mint("MIST0091"),
        Title: "Framework reference value not boolean",
        Template: "Framework reference '{0}' in '{1}' must have a boolean value (true or false).",
        Description: "The value of a [framework_references] entry in mist.toml is a string, number or table where only a boolean is accepted. Write true to enable the shared framework or false to leave it out.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Csproj property diagnostics ──────────────────────────────────────

    public static readonly MistDiagnostic CsprojPropertyTableExpected = new(
        DiagnosticNumber.Mint("MIST0092"),
        Title: "Csproj properties not a table",
        Template: "The 'csproj_properties' field in '{0}' must be a table ([csproj_properties]).",
        Description: "The 'csproj_properties' key in mist.toml holds a scalar or an array where a table is required. Declare it as a table mapping MSBuild property names to string values.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CsprojPropertyEmptyKey = new(
        DiagnosticNumber.Mint("MIST0093"),
        Title: "Csproj property key empty",
        Template: "Property key in '[csproj_properties]' of '{0}' must not be empty or whitespace.",
        Description: "A key under [csproj_properties] in mist.toml is blank, so there is no MSBuild property to set. Remove the entry or give it the property's name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CsprojPropertyInvalidKey = new(
        DiagnosticNumber.Mint("MIST0094"),
        Title: "Csproj property key has invalid characters",
        Template: "Property key '{0}' in '[csproj_properties]' of '{1}' contains invalid characters: {2}.",
        Description: "A key under [csproj_properties] in mist.toml contains characters that are not legal in an MSBuild property name, so the generated .csproj would be malformed. Use letters, digits and underscores only.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CsprojPropertyKeyTooLong = new(
        DiagnosticNumber.Mint("MIST0095"),
        Title: "Csproj property key too long",
        Template: "Property key '{0}' in '[csproj_properties]' of '{1}' exceeds the maximum length of {2} characters (got {3}).",
        Description: "A key under [csproj_properties] in mist.toml is longer than the compiler accepts for an MSBuild property name. Shorten it to the real property name.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CsprojPropertyInvalidValue = new(
        DiagnosticNumber.Mint("MIST0096"),
        Title: "Csproj property value not string",
        Template: "Property '{0}' in '[csproj_properties]' of '{1}' must have a string value.",
        Description: "The value of a [csproj_properties] entry in mist.toml is a number, boolean or table, but MSBuild properties are always text. Quote the value, for example Nullable = \"enable\".",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CsprojPropertyEmptyValue = new(
        DiagnosticNumber.Mint("MIST0097"),
        Title: "Csproj property value empty",
        Template: "Property '{0}' in '[csproj_properties]' of '{1}' has an empty or whitespace value.",
        Description: "The value of a [csproj_properties] entry in mist.toml is a blank string. Give the property a value, or remove the entry if it should not be set.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CsprojPropertyManagedOverride = new(
        DiagnosticNumber.Mint("MIST0098"),
        Title: "Compiler-managed property overridden",
        Template: "Property '{0}' in '[csproj_properties]' of '{1}' overrides a compiler-managed property. This may cause unexpected build behavior.",
        Description: "A [csproj_properties] entry in mist.toml sets a property the compiler itself writes into the generated .csproj, such as the target framework or output type, so the two values can disagree. Remove the entry and use the corresponding mist.toml setting instead, or keep it knowingly and accept the warning.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning);

    // ── Compiler driver diagnostics ──────────────────────────────────────

    public static readonly MistDiagnostic UnknownBackend = new(
        DiagnosticNumber.Mint("MIST0099"),
        Title: "Unknown backend",
        Template: "Unknown backend '{0}'.",
        Description: "The backend named on the command line or in the project configuration is not one the compiler registers. Choose one of the available backends, or omit the option to use the default.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic RuntimeIdentifierUnknown = new(
        DiagnosticNumber.Mint("MIST0100"),
        Title: "Unknown runtime identifier",
        Template: "Runtime identifier '{0}' is not in the SDK's runtime identifier graph.",
        Description: "The runtime identifier requested for the build is not one the installed .NET SDK knows, so no target platform can be selected. Use a known identifier such as linux-x64, win-x64 or osx-arm64, or omit it to build a portable output.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FileError = new(
        DiagnosticNumber.Mint("MIST0101"),
        Title: "Source file unreadable",
        Template: "Failed to read source file '{0}': {1}",
        Description: "A source file listed for compilation could not be read; the trailing message is the operating system's I/O error. Check that the file exists, that it is readable and that no other process holds it locked.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TestFrameworkPackageMissing = new(
        DiagnosticNumber.Mint("MIST0102"),
        Title: "Test framework package missing",
        Template: "Test-attributed methods found but no test framework package is referenced. Add xUnit, NUnit, or MSTest packages to [packages] in mist.toml.",
        Description: "The project declares methods with test attributes, but its [packages] table names no test framework, so 'dotnet test' will discover nothing. Add the xUnit, NUnit or MSTest packages the attributes come from to [packages] in mist.toml.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning);

    public static readonly MistDiagnostic DiagnosticsTableExpected = new(
        DiagnosticNumber.Mint("MIST0103"),
        Title: "Diagnostics field not a table",
        Template: "The 'diagnostics' field in '{path}' must be a table ([diagnostics]).",
        Description: "The manifest sets 'diagnostics' to something other than a table, so the policy it meant to declare cannot be read. Write the policy as a [diagnostics] table with the keys warnings_as_errors, disable and enable.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path)]);

    public static readonly MistDiagnostic DiagnosticsKeyUnknown = new(
        DiagnosticNumber.Mint("MIST0104"),
        Title: "Unknown diagnostics key",
        Template: "'{key}' in '[diagnostics]' of '{path}' is not a key the table takes; it takes warnings_as_errors, disable and enable.",
        Description: "The [diagnostics] table carries a key the compiler does not read, most often a misspelling of one of its three keys. Remove the key or correct it to warnings_as_errors, disable or enable.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("key", SlotKind.Name), new Slot("path", SlotKind.Path)]);

    public static readonly MistDiagnostic DiagnosticsValueInvalid = new(
        DiagnosticNumber.Mint("MIST0105"),
        Title: "Invalid diagnostics value",
        Template: "'{key}' in '[diagnostics]' of '{path}' must be {expected}.",
        Description: "A key of the [diagnostics] table holds a value of the wrong shape. warnings_as_errors takes true, false or an array of diagnostic numbers; disable and enable take an array of diagnostic numbers, each written as a string.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("key", SlotKind.Name), new Slot("path", SlotKind.Path), new Slot("expected", SlotKind.Text)]);

    public static readonly MistDiagnostic DiagnosticsNumberUnknown = new(
        DiagnosticNumber.Mint("MIST0106"),
        Title: "Unknown diagnostic number",
        Template: "'{number}' in '{key}' of '[diagnostics]' in '{path}' is not a diagnostic number the compiler knows.",
        Description: "A [diagnostics] entry names a number that is not in the compiler's registry, so there is no rule for it to act on. Check the number against the diagnostics catalog and correct or remove the entry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("number", SlotKind.Name), new Slot("key", SlotKind.Name), new Slot("path", SlotKind.Path)]);

    public static readonly MistDiagnostic DiagnosticsNumberNotOptional = new(
        DiagnosticNumber.Mint("MIST0107"),
        Title: "Diagnostic cannot be switched",
        Template: "'{number}' in '{key}' of '[diagnostics]' in '{path}' is a {class} diagnostic, which cannot be disabled or enabled; only an Optional rule can be switched.",
        Description: "A disable or enable entry names a diagnostic whose policy class is Fixed or Promotable, and neither class can be switched off: a Fixed diagnostic reports a violated contract and a Promotable one can only be raised. Remove the entry, or list the number under warnings_as_errors if raising it was the intent.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("number", SlotKind.Name), new Slot("key", SlotKind.Name), new Slot("path", SlotKind.Path), new Slot("class", SlotKind.Enum)]);
}