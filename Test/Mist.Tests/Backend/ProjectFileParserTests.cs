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
        _ = Directory.CreateDirectory(dir);
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
            _ = Assert.Single(diags);
            Assert.Equal("MIST0049", diags[0].Number.Value);
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
            _ = Assert.Single(diags);
            Assert.Equal("MIST0052", diags[0].Number.Value);
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
            _ = Assert.Single(diags);
            Assert.Equal("MIST0051", diags[0].Number.Value);
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
            _ = Assert.Single(diags);
            Assert.Equal("MIST0055", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Reference-parsing tests ─────────────────────────────────────────

    [Fact]
    public void ReferenceMissingPath_ReportsMissingPath()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [references]
                BadRef = { description = "no path here" }
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0060", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("mist.toml", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void ReferenceEmptyPath_ReportsEmptyPath()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [references]
                BadRef = { path = "" }
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0061", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("mist.toml", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void NoReferencesSection_ReturnsEmptyReferences()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"
                namespace_root = "MyApp"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Equal("MyApp", result.Name);
            Assert.Empty(result.References);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void ReferenceWithDirectMistTomlPath_StoredAsIs()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [references]
                SharedLib = { path = "libs/SharedLib/mist.toml" }
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.References);

            var expected = Path.GetFullPath(Path.Combine(projectDir, "libs", "SharedLib", "mist.toml"));
            Assert.Equal(expected, result.References[0].Path);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void ReferenceWithDirectoryPath_AppendsMistToml()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [references]
                SharedLib = { path = "libs/SharedLib" }
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.References);

            var expected = Path.GetFullPath(
                Path.Combine(projectDir, "libs", "SharedLib", "mist.toml"));
            Assert.Equal(expected, result.References[0].Path);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void ReferenceWithAbsolutePath_UsedDirectlyAfterCanonicalization()
    {
        var projectDir = CreateTempProjectDir();
        var targetDir = CreateTempProjectDir();
        try
        {
            var absolutePath = Path.Combine(targetDir, "mist.toml");
            // Escape backslashes for TOML string on Windows
            var tomlPath = absolutePath.Replace("\\", "/");

            WriteToml(projectDir, $$"""
                name = "MyApp"

                [references]
                TargetLib = { path = "{{tomlPath}}" }
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.References);

            var expected = Path.GetFullPath(absolutePath);
            Assert.Equal(expected, result.References[0].Path);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void ReferenceWithDotDotSegments_CanonicalizesCorrectly()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [references]
                SharedLib = { path = "../sibling/sub/../SharedLib" }
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.References);

            // The path should be canonicalized: ".." and "sub/.." resolved
            var expected = Path.GetFullPath(
                Path.Combine(projectDir, "..", "sibling", "SharedLib", "mist.toml"));
            Assert.Equal(expected, result.References[0].Path);
            // Verify no ".." segments remain in the resolved path
            Assert.DoesNotContain("..", result.References[0].Path);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}