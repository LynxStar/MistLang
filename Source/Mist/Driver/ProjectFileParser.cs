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
                Location: null,
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
                Location: null,
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
                Location: null,
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
                Location: null,
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
                Location: null,
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
                Location: null,
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
                Location: null,
                CompilationUnitPath: filePath,
                AnalyzerName: null));
            return null;
        }

        return new ProjectConfiguration(name);
    }
}
