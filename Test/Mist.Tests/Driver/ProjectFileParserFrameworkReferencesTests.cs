using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// Unit tests for <see cref="ProjectFileParser"/> [framework_references] table parsing.
/// Validates requirements 4.2–4.8 from the csharp-interop-v2 spec.
/// </summary>
public sealed class ProjectFileParserFrameworkReferencesTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_fwref_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    // ─── Requirement 4.2: true entry included exactly once ───────────────

    [Fact]
    public void FrameworkReferenceTrue_IsIncludedOnce()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Dotted framework names must be quoted in TOML to avoid nested table interpretation
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.FrameworkReferences);
            Assert.Equal("Microsoft.AspNetCore.App", result.FrameworkReferences[0].Name);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.3: false entry excluded ───────────────────────────

    [Fact]
    public void FrameworkReferenceFalse_IsExcluded()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = false
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Empty(result.FrameworkReferences);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void MixedTrueAndFalseEntries_OnlyTrueIncluded()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = true
                "Microsoft.WindowsDesktop.App" = false
                "Microsoft.NETCore.App" = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Equal(2, result.FrameworkReferences.Length);

            var names = result.FrameworkReferences.Select(f => f.Name).ToHashSet();
            Assert.Contains("Microsoft.AspNetCore.App", names);
            Assert.Contains("Microsoft.NETCore.App", names);
            Assert.DoesNotContain("Microsoft.WindowsDesktop.App", names);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.4: Absent table → empty list, no diagnostic ───────

    [Fact]
    public void AbsentFrameworkReferencesTable_ReturnsEmptyListNoDiagnostic()
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
            Assert.Empty(result.FrameworkReferences);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.5: Non-table value → MIST_FWREF_TABLE_EXPECTED ────

    [Fact]
    public void FrameworkReferencesAsString_ReportsTableExpectedAndEmptyList()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"
                framework_references = "not a table"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            // Parser collects the diagnostic and continues with an empty list.
            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_TABLE_EXPECTED", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void FrameworkReferencesAsArray_ReportsTableExpectedAndEmptyList()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"
                framework_references = [1, 2, 3]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_TABLE_EXPECTED", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.6: Non-boolean value → MIST_FWREF_INVALID_VALUE ───

    [Fact]
    public void FrameworkReferenceWithStringValue_ReportsInvalidValueAndExcludes()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = "yes"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_INVALID_VALUE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("Microsoft.AspNetCore.App", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void FrameworkReferenceWithIntegerValue_ReportsInvalidValueAndExcludes()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = 1
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_INVALID_VALUE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void InvalidValueEntry_DoesNotPreventSiblingTrueEntry()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = "yes"
                "Microsoft.NETCore.App" = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);

            // Offending entry excluded; sibling valid entry retained.
            _ = Assert.Single(result.FrameworkReferences);
            Assert.Equal("Microsoft.NETCore.App", result.FrameworkReferences[0].Name);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_INVALID_VALUE", diags[0].Id.Value);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.7: Empty/whitespace name → MIST_FWREF_INVALID_NAME ─

    [Fact]
    public void FrameworkReferenceWithEmptyName_ReportsInvalidNameAndExcludes()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "" = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_INVALID_NAME", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void FrameworkReferenceWithWhitespaceName_ReportsInvalidNameAndExcludes()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "   " = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_INVALID_NAME", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.8: Name >256 chars after trim → MIST_FWREF_INVALID_NAME ─

    [Fact]
    public void FrameworkReferenceNameExceeding256Chars_ReportsInvalidNameAndExcludes()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var longName = new string('a', 257);
            WriteToml(projectDir, $"""
                name = "MyApp"

                [framework_references]
                "{longName}" = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(result.FrameworkReferences);

            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST_FWREF_INVALID_NAME", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("256", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void FrameworkReferenceNameExactly256Chars_IsAccepted()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var name256 = new string('a', 256);
            WriteToml(projectDir, $"""
                name = "MyApp"

                [framework_references]
                "{name256}" = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.FrameworkReferences);
            Assert.Equal(name256, result.FrameworkReferences[0].Name);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 4.2: duplicate-true dedupe (after trimming) ─────────

    [Fact]
    public void DuplicateTrueEntriesAfterTrim_DedupedToSingleEntry()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Two distinct TOML keys that trim to the same framework name.
            // Both are enabled (true); the trimmed name must appear exactly once.
            WriteToml(projectDir, """
                name = "MyApp"

                [framework_references]
                "Microsoft.AspNetCore.App" = true
                "  Microsoft.AspNetCore.App  " = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.FrameworkReferences);
            Assert.Equal("Microsoft.AspNetCore.App", result.FrameworkReferences[0].Name);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}