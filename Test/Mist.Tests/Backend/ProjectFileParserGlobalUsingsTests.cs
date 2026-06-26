using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="ProjectFileParser"/> global usings parsing and validation.
/// </summary>
public sealed class ProjectFileParserGlobalUsingsTests
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

    private static string GetTomlPath(string projectDir)
    {
        return Path.Combine(projectDir, ProjectFileParser.FileName);
    }

    // ─── Valid parsing tests ─────────────────────────────────────────────

    [Fact]
    public void ValidGlobalUsings_WithMultipleNamespaces_ParsesCorrectly()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["System", "System.Collections.Generic", "MyApp.Models"]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Equal(3, result.GlobalUsings.Length);
            Assert.Equal("System", result.GlobalUsings[0]);
            Assert.Equal("System.Collections.Generic", result.GlobalUsings[1]);
            Assert.Equal("MyApp.Models", result.GlobalUsings[2]);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void AbsentGlobalUsingsSection_ProducesEmptyList()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Empty(result.GlobalUsings);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void EmptyNamespacesArray_ProducesEmptyList()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = []
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Empty(result.GlobalUsings);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Error path tests ────────────────────────────────────────────────

    [Fact]
    public void MissingNamespacesField_ReportsMissingNamespacesError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                description = "no namespaces field here"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_MISSING_NAMESPACES", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void EmptyNamespaceEntry_ReportsEmptyNamespaceError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = [""]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_EMPTY_NAMESPACE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void WhitespaceOnlyNamespaceEntry_ReportsEmptyNamespaceError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["   "]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_EMPTY_NAMESPACE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void InvalidCharactersInNamespace_ReportsInvalidNamespaceError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["System::Collections"]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_INVALID_NAMESPACE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void NonArrayNamespacesField_ReportsInvalidFormatError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = "System"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_INVALID_FORMAT", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void NonStringElementInNamespacesArray_ReportsInvalidFormatError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["System", 42, "MyApp.Models"]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_INVALID_FORMAT", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("index 1", diags[0].Message);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Duplicate detection tests ───────────────────────────────────────

    [Fact]
    public void DuplicateEntries_ProducesWarningAndRemovesDuplicates()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["System", "MyApp.Models", "System"]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Equal(2, result.GlobalUsings.Length);
            Assert.Equal("System", result.GlobalUsings[0]);
            Assert.Equal("MyApp.Models", result.GlobalUsings[1]);

            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_GLOBAL_USING_DUPLICATE_NAMESPACE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Warning, diags[0].Severity);
            Assert.Contains("System", diags[0].Message);
            Assert.Contains(GetTomlPath(projectDir), diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Unknown fields test ─────────────────────────────────────────────

    [Fact]
    public void UnknownFieldsInGlobalUsingsSection_AreSilentlyDiscarded()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["System", "MyApp.Models"]
                description = "this is an unknown field"
                version = 2
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Equal(2, result.GlobalUsings.Length);
            Assert.Equal("System", result.GlobalUsings[0]);
            Assert.Equal("MyApp.Models", result.GlobalUsings[1]);

            // No diagnostics at all — unknown fields are silently discarded
            var diags = diagnostics.ToImmutableArray();
            Assert.Empty(diags);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Diagnostic file path inclusion tests ────────────────────────────

    [Theory]
    [InlineData("MIST_GLOBAL_USING_MISSING_NAMESPACES", """
        name = "MyApp"

        [global_usings]
        description = "missing namespaces"
        """)]
    [InlineData("MIST_GLOBAL_USING_EMPTY_NAMESPACE", """
        name = "MyApp"

        [global_usings]
        namespaces = [""]
        """)]
    [InlineData("MIST_GLOBAL_USING_INVALID_NAMESPACE", """
        name = "MyApp"

        [global_usings]
        namespaces = ["System::Bad"]
        """)]
    [InlineData("MIST_GLOBAL_USING_INVALID_FORMAT", """
        name = "MyApp"

        [global_usings]
        namespaces = "not-an-array"
        """)]
    public void AllDiagnostics_IncludeFilePath(string expectedDiagnosticId, string tomlContent)
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, tomlContent);

            var diagnostics = new DiagnosticBag();
            ProjectFileParser.Parse(projectDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();
            Assert.NotEmpty(diags);

            var diag = diags.First(d => d.Id.Value == expectedDiagnosticId);
            Assert.Contains(GetTomlPath(projectDir), diag.Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void DuplicateWarning_IncludesFilePath()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [global_usings]
                namespaces = ["System", "System"]
                """);

            var diagnostics = new DiagnosticBag();
            ProjectFileParser.Parse(projectDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();
            Assert.NotEmpty(diags);

            var diag = diags.First(d => d.Id.Value == "MIST_GLOBAL_USING_DUPLICATE_NAMESPACE");
            Assert.Contains(GetTomlPath(projectDir), diag.Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}