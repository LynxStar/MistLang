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
        if (!File.Exists(filePath))
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
        catch (Exception ex)
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
        catch (TomlException ex)
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
        if (!model.TryGetValue("name", out var nameObj) || nameObj is not string)
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
        if (string.IsNullOrWhiteSpace(name))
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
        if (!ValidNamePattern().IsMatch(name))
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
        if (name.Length > MaxNameLength)
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
        if (model.TryGetValue("namespace_root", out var nsRootObj) && nsRootObj is string nsRootStr)
        {
            // 8a. non-empty/non-whitespace check
            if (string.IsNullOrWhiteSpace(nsRootStr))
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
            if (!ValidNamePattern().IsMatch(nsRootStr))
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
        if (model.TryGetValue("entry_point", out var epObj) && epObj is string epStr)
        {
            if (string.IsNullOrWhiteSpace(epStr))
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

        if (model.TryGetValue("references", out var refsObj))
        {
            if (refsObj is not TomlTable refsTable)
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

            foreach (var (refName, refValue) in refsTable)
            {
                // 9a. Validate reference key name (empty → pattern → length)
                if (string.IsNullOrWhiteSpace(refName))
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

                if (!ValidNamePattern().IsMatch(refName))
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

                if (refName.Length > MaxNameLength)
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
                if (refValue is not TomlTable refEntry)
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
                if (!refEntry.TryGetValue("path", out var pathObj) || pathObj is not string rawPath)
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

                if (string.IsNullOrWhiteSpace(rawPath))
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
                if (Path.IsPathRooted(rawPath))
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
                if (!Path.GetFileName(normalizedPath)
                         .Equals(FileName, StringComparison.Ordinal))
                {
                    normalizedPath = Path.Combine(normalizedPath, FileName);
                }

                references.Add(new ProjectReference(refName, normalizedPath));
            }
        }

        // 10. Read [global_usings] section (optional)
        var globalUsings = ImmutableArray<string>.Empty;

        if (model.TryGetValue("global_usings", out var guObj))
        {
            if (guObj is not TomlTable guTable)
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

            if (!guTable.TryGetValue("namespaces", out var nsObj))
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

            if (nsObj is not TomlArray nsArray)
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

            for (var i = 0; i < nsArray.Count; i++)
            {
                if (nsArray[i] is not string entry)
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

                if (string.IsNullOrWhiteSpace(entry))
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

                if (!ValidNamespacePattern().IsMatch(entry))
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

                if (!seen.Add(entry))
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

        return new ProjectConfiguration(name, namespaceRoot, references.ToImmutable(), entryPoint, globalUsings);
    }
}
