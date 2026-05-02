using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Example-based unit tests for <see cref="ProjectReferenceResolver"/>.
/// Each test creates real temp directories with mist.toml files on disk
/// and calls <see cref="ProjectReferenceResolver.Resolve"/> directly.
/// </summary>
public sealed class ProjectReferenceResolverTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_ref_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    /// <summary>
    /// Builds a <see cref="ProjectReference"/> pointing at the given directory's mist.toml.
    /// </summary>
    private static ProjectReference RefTo(string projectDir)
    {
        return new ProjectReference(
            Path.GetFullPath(Path.Combine(projectDir, ProjectFileParser.FileName)));
    }

    // ─── Error path tests ────────────────────────────────────────────────

    [Fact]
    public void NonExistentPath_ReportsReferenceNotFound()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);

            var bogusPath = Path.Combine(Path.GetTempPath(), $"mist_no_exist_{Guid.NewGuid():N}", "mist.toml");
            var references = ImmutableArray.Create(new ProjectReference(bogusPath));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_REFERENCE_NOT_FOUND", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(bogusPath, diags[0].Message);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public void DirectoryWithoutMistToml_ReportsNoProjectFile()
    {
        var rootDir = CreateTempDir();
        var emptyDir = CreateTempDir(); // exists but has no mist.toml
        try
        {
            WriteToml(rootDir, """name = "Root" """);

            var references = ImmutableArray.Create(RefTo(emptyDir));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_REFERENCE_NO_PROJECT_FILE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains(emptyDir, diags[0].Message);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void FileNotNamedMistToml_ReportsReferenceNotFound()
    {
        var rootDir = CreateTempDir();
        var otherDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);

            // Create a file that is NOT named mist.toml
            var wrongFile = Path.Combine(otherDir, "project.toml");
            File.WriteAllText(wrongFile, """name = "Other" """);

            var references = ImmutableArray.Create(new ProjectReference(wrongFile));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_REFERENCE_NOT_FOUND", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(otherDir, recursive: true);
        }
    }

    [Fact]
    public void InvalidReferencedMistToml_PropagatesInnerParseError()
    {
        var rootDir = CreateTempDir();
        var badDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);

            // Write an invalid mist.toml (missing required 'name' field)
            WriteToml(badDir, """version = "1.0.0" """);

            var references = ImmutableArray.Create(RefTo(badDir));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            // The inner parse error for missing name should be propagated
            Assert.Equal("MIST_PROJECT_MISSING_NAME", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(badDir, recursive: true);
        }
    }

    // ─── Diamond dependency test ─────────────────────────────────────────

    [Fact]
    public void DiamondDependency_EachProjectAppearsExactlyOnce()
    {
        // Graph: Root → B, Root → C, B → D, C → D
        var rootDir = CreateTempDir();
        var dirB = CreateTempDir();
        var dirC = CreateTempDir();
        var dirD = CreateTempDir();
        try
        {
            // D has no references
            WriteToml(dirD, """name = "D" """);
            File.WriteAllText(Path.Combine(dirD, "d.mist"), "// D source");

            // B references D
            var dTomlPath = Path.GetFullPath(Path.Combine(dirD, "mist.toml")).Replace("\\", "/");
            WriteToml(dirB, $"""
                name = "B"

                [[references]]
                path = "{dTomlPath}"
                """);
            File.WriteAllText(Path.Combine(dirB, "b.mist"), "// B source");

            // C references D
            WriteToml(dirC, $"""
                name = "C"

                [[references]]
                path = "{dTomlPath}"
                """);
            File.WriteAllText(Path.Combine(dirC, "c.mist"), "// C source");

            // Root references B and C
            WriteToml(rootDir, """name = "Root" """);

            var references = ImmutableArray.Create(RefTo(dirB), RefTo(dirC));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.False(diagnostics.HasErrors);

            // D, B, C should each appear exactly once
            Assert.Equal(3, result.Length);

            var canonicalPaths = result.Select(r => r.CanonicalTomlPath).ToList();
            Assert.Equal(canonicalPaths.Distinct().Count(), canonicalPaths.Count);

            // D should appear before B (D is B's dependency)
            var dIndex = canonicalPaths.IndexOf(
                Path.GetFullPath(Path.Combine(dirD, "mist.toml")));
            var bIndex = canonicalPaths.IndexOf(
                Path.GetFullPath(Path.Combine(dirB, "mist.toml")));
            var cIndex = canonicalPaths.IndexOf(
                Path.GetFullPath(Path.Combine(dirC, "mist.toml")));

            Assert.True(dIndex >= 0, "D should be in the result");
            Assert.True(bIndex >= 0, "B should be in the result");
            Assert.True(cIndex >= 0, "C should be in the result");
            Assert.True(dIndex < bIndex, "D should appear before B (deepest first)");
            Assert.True(dIndex < cIndex, "D should appear before C (deepest first)");
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(dirB, recursive: true);
            Directory.Delete(dirC, recursive: true);
            Directory.Delete(dirD, recursive: true);
        }
    }

    // ─── Cycle detection tests ───────────────────────────────────────────

    [Fact]
    public void DirectSelfReference_ReportsCircularReference()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);

            // Root references itself
            var references = ImmutableArray.Create(RefTo(rootDir));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_CIRCULAR_REFERENCE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public void TwoNodeCycle_ReportsCircularReferenceWithBothNodes()
    {
        // Graph: Root → A → B → A (cycle between A and B)
        var rootDir = CreateTempDir();
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        try
        {
            // A references B
            var bTomlPath = Path.GetFullPath(Path.Combine(dirB, "mist.toml")).Replace("\\", "/");
            WriteToml(dirA, $"""
                name = "A"

                [[references]]
                path = "{bTomlPath}"
                """);

            // B references A (creating the cycle)
            var aTomlPath = Path.GetFullPath(Path.Combine(dirA, "mist.toml")).Replace("\\", "/");
            WriteToml(dirB, $"""
                name = "B"

                [[references]]
                path = "{aTomlPath}"
                """);

            WriteToml(rootDir, """name = "Root" """);

            var references = ImmutableArray.Create(RefTo(dirA));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            Assert.Single(diags);
            Assert.Equal("MIST_PROJECT_CIRCULAR_REFERENCE", diags[0].Id.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);

            // The message should contain both A and B paths
            var aCanonical = Path.GetFullPath(Path.Combine(dirA, "mist.toml"));
            var bCanonical = Path.GetFullPath(Path.Combine(dirB, "mist.toml"));
            Assert.Contains(aCanonical, diags[0].Message);
            Assert.Contains(bCanonical, diags[0].Message);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(dirA, recursive: true);
            Directory.Delete(dirB, recursive: true);
        }
    }

    // ─── Multiple independent errors test ────────────────────────────────

    [Fact]
    public void MultipleIndependentInvalidReferences_AllErrorsReported()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);

            // Create 3 independent non-existent paths
            var bogus1 = Path.Combine(Path.GetTempPath(), $"mist_bogus1_{Guid.NewGuid():N}", "mist.toml");
            var bogus2 = Path.Combine(Path.GetTempPath(), $"mist_bogus2_{Guid.NewGuid():N}", "mist.toml");
            var bogus3 = Path.Combine(Path.GetTempPath(), $"mist_bogus3_{Guid.NewGuid():N}", "mist.toml");

            var references = ImmutableArray.Create(
                new ProjectReference(bogus1),
                new ProjectReference(bogus2),
                new ProjectReference(bogus3));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();

            // All 3 errors should be reported, not just the first
            Assert.Equal(3, diags.Length);
            Assert.All(diags, d =>
            {
                Assert.Equal("MIST_PROJECT_REFERENCE_NOT_FOUND", d.Id.Value);
                Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            });

            // Each diagnostic should reference a different path
            var messages = diags.Select(d => d.Message).ToList();
            Assert.Contains(messages, m => m.Contains(bogus1));
            Assert.Contains(messages, m => m.Contains(bogus2));
            Assert.Contains(messages, m => m.Contains(bogus3));
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }
}
