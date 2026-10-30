using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// Unit tests for <see cref="ProjectFileParser"/> [packages] table parsing.
/// Validates requirements 1.1–1.8 from the Assembly Type Provider spec.
/// </summary>
public sealed class ProjectFileParserPackagesTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_pkg_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    // ─── Requirement 1.5: Absent [packages] table → empty package list ──

    [Fact]
    public void AbsentPackagesTable_ReturnsEmptyPackageList()
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
            Assert.Empty(result.Packages);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 1.6: Non-table [packages] → error diagnostic ───────

    [Fact]
    public void PackagesFieldNotATable_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"
                packages = "not a table"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0083", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void PackagesFieldAsArray_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"
                packages = [1, 2, 3]
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.Null(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0083", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 1.7: Non-string version value → error diagnostic ───

    [Fact]
    public void PackageVersionAsInteger_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                SomePackage = 123
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0086", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("SomePackage", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void PackageVersionAsBoolean_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                SomePackage = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0086", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 1.1: Valid package entries parse correctly ──────────

    [Fact]
    public void ValidSinglePackage_ParsesCorrectly()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Dotted package names must be quoted in TOML to avoid nested table interpretation
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                "Newtonsoft.Json" = "13.0.3"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.Packages);
            Assert.Equal("Newtonsoft.Json", result.Packages[0].Name);
            Assert.Equal("13.0.3", result.Packages[0].Version);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void ValidMultiplePackages_ParsesAllCorrectly()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Dotted package names must be quoted in TOML to avoid nested table interpretation
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                "Newtonsoft.Json" = "13.0.3"
                "System.Text.Json" = "8.0.0"
                My-Package_v2 = "1.0.0-beta"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            Assert.Equal(3, result.Packages.Length);

            var packagesByName = result.Packages.ToDictionary(p => p.Name, p => p.Version);
            Assert.Equal("13.0.3", packagesByName["Newtonsoft.Json"]);
            Assert.Equal("8.0.0", packagesByName["System.Text.Json"]);
            Assert.Equal("1.0.0-beta", packagesByName["My-Package_v2"]);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void PackageNameWithAllValidCharacters_ParsesCorrectly()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Name uses all allowed character classes: letters, digits, hyphens, underscores, periods
            // Dotted names must be quoted in TOML
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                "A-b_c.D0" = "1.0.0"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.Packages);
            Assert.Equal("A-b_c.D0", result.Packages[0].Name);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 1.8: Invalid package name characters → error ────────

    [Theory]
    [InlineData("Bad Package")]
    [InlineData("Bad@Package")]
    [InlineData("Bad#Package")]
    [InlineData("Bad/Package")]
    [InlineData("Bad!Package")]
    public void PackageNameWithInvalidCharacters_ReportsError(string packageName)
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            // Use inline table syntax to avoid TOML key parsing issues with special chars
            WriteToml(projectDir, $"""
                name = "MyApp"

                [packages]
                "{packageName}" = "1.0.0"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            // The parser continues after invalid package names (uses continue)
            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0084", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(packageName, diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 1.8: Package name exceeding 128 chars → error ───────

    [Fact]
    public void PackageNameExceeding128Chars_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var longName = new string('a', 129);
            WriteToml(projectDir, $"""
                name = "MyApp"

                [packages]
                {longName} = "1.0.0"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0085", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("128", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void PackageNameExactly128Chars_IsAccepted()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var name128 = new string('a', 128);
            WriteToml(projectDir, $"""
                name = "MyApp"

                [packages]
                {name128} = "1.0.0"
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            Assert.False(diagnostics.HasErrors);
            _ = Assert.Single(result.Packages);
            Assert.Equal(name128, result.Packages[0].Name);
            Assert.Equal("1.0.0", result.Packages[0].Version);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Requirement 1.4: Empty/whitespace version → error diagnostic ────

    [Fact]
    public void PackageWithEmptyVersion_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                SomePackage = ""
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0087", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("SomePackage", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void PackageWithWhitespaceOnlyVersion_ReportsError()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, """
                name = "MyApp"

                [packages]
                SomePackage = "   "
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0087", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("SomePackage", diags[0].Message);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Multiple errors: parser continues past invalid entries ───────────

    [Fact]
    public void MultipleInvalidPackages_ReportsAllErrors()
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var longName = new string('x', 129);
            WriteToml(projectDir, $"""
                name = "MyApp"

                [packages]
                ValidPackage = "1.0.0"
                {longName} = "2.0.0"
                AnotherValid = ""
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            Assert.NotNull(result);
            var diags = diagnostics.ToImmutableArray();
            Assert.Equal(2, diags.Length);

            // One error for the long name, one for the empty version
            Assert.Contains(diags, d => d.Number.Value == "MIST0085");
            Assert.Contains(diags, d => d.Number.Value == "MIST0087");

            // The valid package should still be parsed
            _ = Assert.Single(result.Packages);
            Assert.Equal("ValidPackage", result.Packages[0].Name);
            Assert.Equal("1.0.0", result.Packages[0].Version);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}