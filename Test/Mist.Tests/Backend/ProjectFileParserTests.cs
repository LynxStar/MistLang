using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="ProjectFileParser"/> error paths and boundary conditions.
/// </summary>
public sealed class ProjectFileParserTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_unit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    // ─── Error path tests ────────────────────────────────────────────────

    [Fact]
    public void MissingToml_ReportsFileNotFound()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Do NOT create mist.toml — the directory is empty
            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_FILE_NOT_FOUND", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void TomlWithoutNameKey_ReportsMissingName()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, "version = \"1.0.0\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_MISSING_NAME", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void MalformedToml_ReportsTomlError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, "name = [[[invalid toml content");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_TOML_ERROR", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void NameExactly128Chars_IsAccepted()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var name = new string('a', 128);
            WriteToml(projectDir, $"name = \"{name}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Equal(name, result.Name);
            Assert.False(diagnostics.HasErrors);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void Name129Chars_ReportsNameTooLong()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var name = new string('a', 129);
            WriteToml(projectDir, $"name = \"{name}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_NAME_TOO_LONG", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}
