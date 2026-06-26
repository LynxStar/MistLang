using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace Mist.Driver;

/// <summary>
/// Reads and validates a <c>mist.toml</c> project file, producing a
/// <see cref="ProjectConfiguration"/> or reporting diagnostics.
/// </summary>
public static partial class ProjectFileParser
{
    public const string FileName = "mist.toml";
    public const int MaxNameLength = 128;

    /// <summary>
    /// Maximum length, after trimming, of a framework name declared in the
    /// <c>[framework_references]</c> table.
    /// </summary>
    public const int MaxFrameworkReferenceNameLength = 256;

    /// <summary>
    /// Property names that are managed by the compiler and emitted in the hardcoded
    /// PropertyGroup. Users may override them, but a warning is emitted.
    /// </summary>
    private static readonly HashSet<string> CompilerManagedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputType",
        "TargetFramework",
        "ImplicitUsings",
        "Nullable",
        "Deterministic",
        "EnableNETAnalyzers"
    };

    [GeneratedRegex(@"^[a-zA-Z0-9\-_\.]+$")]
    private static partial Regex ValidNamePattern();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$")]
    private static partial Regex ValidNamespacePattern();

    /// <summary>
    /// Parses the <c>mist.toml</c> file from the given project folder.
    /// Returns <c>null</c> and populates diagnostics on any error.
    /// </summary>
    public static ProjectConfiguration? Parse(string projectFolder, DiagnosticBag diagnostics)
    {
        var filePath = Path.Combine(projectFolder, FileName);

        // 1. File existence check
        if(!File.Exists(filePath))
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_FILE_NOT_FOUND"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Project file '{filePath}' not found.",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        // 2. File read with I/O error handling
        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch(Exception ex)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_FILE_READ_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Failed to read project file '{filePath}': {ex.Message}",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        // 3. TOML parsing
        TomlTable model;
        try
        {
            model = Toml.ToModel(content);
        }
        catch(TomlException ex)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_TOML_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Failed to parse project file '{filePath}': {ex.Message}",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        // 4. name field presence check
        if(!model.TryGetValue("name", out var nameObj) || nameObj is not string)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_MISSING_NAME"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Project file '{filePath}' is missing the required 'name' field.",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        var name = (string)nameObj;

        // 5. name non-empty/non-whitespace check
        if(string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_EMPTY_NAME"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Project name in '{filePath}' must not be empty or whitespace.",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        // 6. name character validation
        if(!ValidNamePattern().IsMatch(name))
        {
            var invalidChars = name
                .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                .Distinct()
                .OrderBy(c => c);
            var invalidList = string.Join(", ", invalidChars.Select(c => $"'{c}'"));

            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_INVALID_NAME"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Project name in '{filePath}' contains invalid characters: {invalidList}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        // 7. name length check
        if(name.Length > MaxNameLength)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PROJECT_NAME_TOO_LONG"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Project name in '{filePath}' exceeds the maximum length of {MaxNameLength} characters (got {name.Length}).",
                Location: default,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        // 8. namespace_root field (optional)
        string namespaceRoot;
        if(model.TryGetValue("namespace_root", out var nsRootObj) && nsRootObj is string nsRootStr)
        {
            // 8a. non-empty/non-whitespace check
            if(string.IsNullOrWhiteSpace(nsRootStr))
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_PROJECT_EMPTY_NAMESPACE_ROOT"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Namespace root in '{filePath}' must not be empty or whitespace when specified.",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            // 8b. character validation (same regex as name)
            if(!ValidNamePattern().IsMatch(nsRootStr))
            {
                var invalidChars = nsRootStr
                    .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                    .Distinct()
                    .OrderBy(c => c);
                var invalidList = string.Join(", ", invalidChars.Select(c => $"'{c}'"));

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_PROJECT_INVALID_NAMESPACE_ROOT"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Namespace root in '{filePath}' contains invalid characters: {invalidList}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            namespaceRoot = nsRootStr;
        }
        else
        {
            // 8c. default to name when namespace_root is absent
            namespaceRoot = name;
        }

        // 8.5. entry_point field (optional)
        string? entryPoint = null;
        if(model.TryGetValue("entry_point", out var epObj) && epObj is string epStr)
        {
            if(string.IsNullOrWhiteSpace(epStr))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.ProjectEmptyEntryPoint,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Entry point in '{filePath}' must not be empty or whitespace when specified.",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            entryPoint = epStr;
        }

        // 9. Read [references] table (optional)
        var references = ImmutableArray.CreateBuilder<ProjectReference>();

        if(model.TryGetValue("references", out var refsObj))
        {
            if(refsObj is not TomlTable refsTable)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.ProjectReferenceInvalidFormat,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The 'references' field in '{filePath}' must be a table ([references]).",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            foreach(var (refName, refValue) in refsTable)
            {
                // 9a. Validate reference key name (empty → pattern → length)
                if(string.IsNullOrWhiteSpace(refName))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.ProjectReferenceInvalidName,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Reference key '{refName}' in '{filePath}' must not be empty or whitespace.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                if(!ValidNamePattern().IsMatch(refName))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.ProjectReferenceInvalidName,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Reference key '{refName}' in '{filePath}' contains invalid characters. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                if(refName.Length > MaxNameLength)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.ProjectReferenceInvalidName,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Reference key '{refName}' in '{filePath}' exceeds the maximum length of {MaxNameLength} characters (got {refName.Length}).",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                // 9b. Validate reference value is a table
                if(refValue is not TomlTable refEntry)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.ProjectReferenceInvalidFormat,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Reference '{refName}' in '{filePath}' must be a table (e.g., {refName} = {{ path = \"...\" }}).",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                // 9c. Extract and validate 'path' field
                if(!refEntry.TryGetValue("path", out var pathObj) || pathObj is not string rawPath)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.ProjectReferenceMissingPath,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Reference '{refName}' in '{filePath}' is missing the required 'path' field.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                if(string.IsNullOrWhiteSpace(rawPath))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.ProjectReferenceEmptyPath,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Reference '{refName}' in '{filePath}' has an empty or whitespace 'path' field.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                // 9d. Normalize path (unchanged logic)
                string normalizedPath;
                if(Path.IsPathRooted(rawPath))
                {
                    normalizedPath = Path.GetFullPath(rawPath);
                }
                else
                {
                    normalizedPath = Path.GetFullPath(Path.Combine(projectFolder, rawPath));
                }

                // If normalized path does not already end with "mist.toml",
                // treat it as a directory and append "mist.toml".
                // Existence checks are NOT performed here — deferred to resolver.
                if(!Path.GetFileName(normalizedPath)
                         .Equals(FileName, StringComparison.Ordinal))
                {
                    normalizedPath = Path.Combine(normalizedPath, FileName);
                }

                references.Add(new ProjectReference(refName, normalizedPath));
            }
        }

        // 10. Read [global_usings] section (optional)
        var globalUsings = ImmutableArray<string>.Empty;

        if(model.TryGetValue("global_usings", out var guObj))
        {
            if(guObj is not TomlTable guTable)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_GLOBAL_USING_INVALID_FORMAT"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The '[global_usings]' section in '{filePath}' must be a table.",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            if(!guTable.TryGetValue("namespaces", out var nsObj))
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_GLOBAL_USING_MISSING_NAMESPACES"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The '[global_usings]' section in '{filePath}' is missing the required 'namespaces' field.",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            if(nsObj is not TomlArray nsArray)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_GLOBAL_USING_INVALID_FORMAT"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The 'namespaces' field in '[global_usings]' section of '{filePath}' must be an array.",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            var builder = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for(var i = 0; i < nsArray.Count; i++)
            {
                if(nsArray[i] is not string entry)
                {
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_GLOBAL_USING_INVALID_FORMAT"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Element at index {i} in 'namespaces' array of '{filePath}' must be a string.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                if(string.IsNullOrWhiteSpace(entry))
                {
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_GLOBAL_USING_EMPTY_NAMESPACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Namespace entry at index {i} in '{filePath}' must not be empty or whitespace.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                if(!ValidNamespacePattern().IsMatch(entry))
                {
                    var invalidChars = entry
                        .Where(c => !char.IsLetterOrDigit(c) && c != '_' && c != '.')
                        .Distinct()
                        .OrderBy(c => c);
                    var invalidList = string.Join(", ", invalidChars.Select(c => $"'{c}'"));

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_GLOBAL_USING_INVALID_NAMESPACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Namespace entry '{entry}' in '{filePath}' contains invalid characters: {invalidList}.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    return null;
                }

                if(!seen.Add(entry))
                {
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_GLOBAL_USING_DUPLICATE_NAMESPACE"),
                        DiagnosticSeverity.Warning,
                        PipelineStage.Driver,
                        $"Duplicate namespace '{entry}' in '{filePath}' will be ignored.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    continue;
                }

                builder.Add(entry);
            }

            globalUsings = builder.ToImmutable();
        }

        // 11. Read [packages] table (optional)
        var packages = ImmutableArray<NuGetPackageReference>.Empty;

        if(model.TryGetValue("packages", out var pkgsObj))
        {
            if(pkgsObj is not TomlTable pkgsTable)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_PKG_TABLE_EXPECTED"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The 'packages' field in '{filePath}' must be a table ([packages]).",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
                return null;
            }

            var pkgBuilder = ImmutableArray.CreateBuilder<NuGetPackageReference>();

            foreach(var (pkgName, pkgValue) in pkgsTable)
            {
                // 11a. Validate package name: pattern check
                if(!ValidNamePattern().IsMatch(pkgName))
                {
                    var invalidChars = pkgName
                        .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                        .Distinct()
                        .OrderBy(c => c);
                    var invalidList = string.Join(", ", invalidChars.Select(c => $"'{c}'"));

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_PKG_INVALID_NAME"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Package name '{pkgName}' in '{filePath}' contains invalid characters: {invalidList}. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    continue;
                }

                // 11b. Validate package name: length check
                if(pkgName.Length > MaxNameLength)
                {
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_PKG_INVALID_NAME"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Package name '{pkgName}' in '{filePath}' exceeds the maximum length of {MaxNameLength} characters (got {pkgName.Length}).",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    continue;
                }

                // 11c. Validate version value: must be a non-empty string
                if(pkgValue is not string versionStr)
                {
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_PKG_INVALID_VERSION"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Package '{pkgName}' in '{filePath}' must have a string version value.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    continue;
                }

                if(string.IsNullOrWhiteSpace(versionStr))
                {
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_PKG_INVALID_VERSION"),
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Package '{pkgName}' in '{filePath}' has an empty or whitespace version string.",
                        Location: default,
                        CompilationUnitPath: filePath,
                        AnalyzerName: null));
                    continue;
                }

                pkgBuilder.Add(new NuGetPackageReference(pkgName, versionStr));
            }

            packages = pkgBuilder.ToImmutable();
        }

        // 12. Read target_framework field (optional string), defaulting to net10.0
        var targetFramework = "net10.0";
        if(model.TryGetValue("target_framework", out var tfObj)
            && tfObj is string tfStr
            && !string.IsNullOrWhiteSpace(tfStr))
        {
            targetFramework = tfStr.Trim();
        }

        // 13. Read [framework_references] table (optional)
        var frameworkReferences = ImmutableArray<FrameworkReference>.Empty;

        if(model.TryGetValue("framework_references", out var fwrefsObj))
        {
            if(fwrefsObj is not TomlTable fwrefsTable)
            {
                // Present but not a table → emit diagnostic, produce empty list,
                // and continue (do not abort the parse).
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_FWREF_TABLE_EXPECTED"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The 'framework_references' field in '{filePath}' must be a table ([framework_references]).",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
            }
            else
            {
                var fwrefBuilder = ImmutableArray.CreateBuilder<FrameworkReference>();
                var seenFrameworks = new HashSet<string>(StringComparer.Ordinal);

                foreach(var (rawFwName, fwValue) in fwrefsTable)
                {
                    var fwName = rawFwName.Trim();

                    // 13a. Validate framework name: empty/whitespace after trim
                    if(fwName.Length == 0)
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_FWREF_INVALID_NAME"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Framework reference name '{rawFwName}' in '{filePath}' must not be empty or whitespace after trimming.",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 13b. Validate framework name: length check (after trim)
                    if(fwName.Length > MaxFrameworkReferenceNameLength)
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_FWREF_INVALID_NAME"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Framework reference name '{fwName}' in '{filePath}' exceeds the maximum length of {MaxFrameworkReferenceNameLength} characters (got {fwName.Length}).",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 13c. Validate value: must be a TOML boolean
                    if(fwValue is not bool enabled)
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_FWREF_INVALID_VALUE"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Framework reference '{fwName}' in '{filePath}' must have a boolean value (true or false).",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 13d. false → exclude from the list.
                    if(!enabled)
                    {
                        continue;
                    }

                    // 13e. true → include exactly once (dedupe by trimmed name).
                    if(seenFrameworks.Add(fwName))
                    {
                        fwrefBuilder.Add(new FrameworkReference(fwName));
                    }
                }

                frameworkReferences = fwrefBuilder.ToImmutable();
            }
        }

        // 14. Read [csproj_properties] table (optional)
        var csprojProperties = ImmutableArray<CsprojProperty>.Empty;

        if(model.TryGetValue("csproj_properties", out var csprojPropsObj))
        {
            if(csprojPropsObj is not TomlTable csprojPropsTable)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_CSPROJ_PROP_TABLE_EXPECTED"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"The 'csproj_properties' field in '{filePath}' must be a table ([csproj_properties]).",
                    Location: default,
                    CompilationUnitPath: filePath,
                    AnalyzerName: null));
            }
            else
            {
                var csprojPropBuilder = ImmutableArray.CreateBuilder<CsprojProperty>();

                foreach(var (rawKey, propValue) in csprojPropsTable)
                {
                    // 14a. Validate key: empty/whitespace
                    if(string.IsNullOrWhiteSpace(rawKey))
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CSPROJ_PROP_EMPTY_KEY"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Property key in '[csproj_properties]' of '{filePath}' must not be empty or whitespace.",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 14b. Validate key: character pattern
                    if(!ValidNamePattern().IsMatch(rawKey))
                    {
                        var invalidChars = rawKey
                            .Where(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                            .Distinct()
                            .OrderBy(c => c);
                        var invalidList = string.Join(", ", invalidChars.Select(c => $"'{c}'"));

                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CSPROJ_PROP_INVALID_KEY"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Property key '{rawKey}' in '[csproj_properties]' of '{filePath}' contains invalid characters: {invalidList}.",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 14c. Validate key: length check
                    if(rawKey.Length > MaxNameLength)
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CSPROJ_PROP_KEY_TOO_LONG"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Property key '{rawKey[..32]}...' in '[csproj_properties]' of '{filePath}' exceeds the maximum length of {MaxNameLength} characters (got {rawKey.Length}).",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 14d. Validate value: must be a string
                    if(propValue is not string valueStr)
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CSPROJ_PROP_INVALID_VALUE"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Property '{rawKey}' in '[csproj_properties]' of '{filePath}' must have a string value.",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 14e. Validate value: non-empty/non-whitespace
                    if(string.IsNullOrWhiteSpace(valueStr))
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CSPROJ_PROP_EMPTY_VALUE"),
                            DiagnosticSeverity.Error,
                            PipelineStage.Driver,
                            $"Property '{rawKey}' in '[csproj_properties]' of '{filePath}' has an empty or whitespace value.",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                        continue;
                    }

                    // 14f. Warn if key shadows a compiler-managed property
                    if(CompilerManagedProperties.Contains(rawKey))
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CSPROJ_PROP_MANAGED_OVERRIDE"),
                            DiagnosticSeverity.Warning,
                            PipelineStage.Driver,
                            $"Property '{rawKey}' in '[csproj_properties]' of '{filePath}' overrides a compiler-managed property. This may cause unexpected build behavior.",
                            Location: default,
                            CompilationUnitPath: filePath,
                            AnalyzerName: null));
                    }

                    csprojPropBuilder.Add(new CsprojProperty(rawKey, valueStr));
                }

                csprojProperties = csprojPropBuilder.ToImmutable();
            }
        }

        return new ProjectConfiguration(name, namespaceRoot, references.ToImmutable(), entryPoint, globalUsings, packages, frameworkReferences, targetFramework, csprojProperties);
    }
}