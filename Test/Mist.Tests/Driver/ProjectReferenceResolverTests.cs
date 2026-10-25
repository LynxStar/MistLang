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
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    /// <summary>
    /// Builds a <see cref="ProjectReference"/> pointing at the given directory's mist.toml.
    /// The <paramref name="name"/> should match the target project's <c>name</c> field in its TOML.
    /// </summary>
    private static ProjectReference RefTo(string name, string projectDir)
    {
        var path = Path.GetFullPath(Path.Combine(projectDir, ProjectFileParser.FileName));
        return new ProjectReference(name, path);
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
            var references = ImmutableArray.Create(new ProjectReference("Bogus", bogusPath));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0062", diags[0].Number.Value);
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

            var references = ImmutableArray.Create(RefTo("EmptyProject", emptyDir));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0063", diags[0].Number.Value);
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

            var references = ImmutableArray.Create(new ProjectReference("Other", wrongFile));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0062", diags[0].Number.Value);
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

            var references = ImmutableArray.Create(RefTo("BadProject", badDir));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            // The inner parse error for missing name should be propagated
            Assert.Equal("MIST0052", diags[0].Number.Value);
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
            WriteToml(dirB, $$"""
                name = "B"

                [references]
                D = { path = "{{dTomlPath}}" }
                """);
            File.WriteAllText(Path.Combine(dirB, "b.mist"), "// B source");

            // C references D
            WriteToml(dirC, $$"""
                name = "C"

                [references]
                D = { path = "{{dTomlPath}}" }
                """);
            File.WriteAllText(Path.Combine(dirC, "c.mist"), "// C source");

            // Root references B and C
            WriteToml(rootDir, """name = "Root" """);

            var references = ImmutableArray.Create(RefTo("B", dirB), RefTo("C", dirC));

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
            var references = ImmutableArray.Create(RefTo("Root", rootDir));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0064", diags[0].Number.Value);
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
            WriteToml(dirA, $$"""
                name = "A"

                [references]
                B = { path = "{{bTomlPath}}" }
                """);

            // B references A (creating the cycle)
            var aTomlPath = Path.GetFullPath(Path.Combine(dirA, "mist.toml")).Replace("\\", "/");
            WriteToml(dirB, $$"""
                name = "B"

                [references]
                A = { path = "{{aTomlPath}}" }
                """);

            WriteToml(rootDir, """name = "Root" """);

            var references = ImmutableArray.Create(RefTo("A", dirA));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0064", diags[0].Number.Value);
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

    // ─── Name-mismatch tests ────────────────────────────────────────────

    [Fact]
    public void NameMismatch_ReportsProjectReferenceNameMismatch()
    {
        var rootDir = CreateTempDir();
        var targetDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            WriteToml(targetDir, """name = "CorrectName" """);
            File.WriteAllText(Path.Combine(targetDir, "lib.mist"), "// source");

            // Reference key is "WrongName" but target project's name is "CorrectName"
            var targetTomlPath = Path.GetFullPath(Path.Combine(targetDir, "mist.toml"));
            var references = ImmutableArray.Create(new ProjectReference("WrongName", targetTomlPath));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0070", diags[0].Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
            Assert.Contains("WrongName", diags[0].Message);
            Assert.Contains("CorrectName", diags[0].Message);

            // The mismatched reference should NOT appear in the resolved output
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void NameMismatch_TransitiveDepsAreNotTraversed()
    {
        // Graph: Root → "WrongName" (actual name "CorrectName") → "Transitive"
        // Because "WrongName" mismatches, "Transitive" should NOT be resolved.
        var rootDir = CreateTempDir();
        var mismatchedDir = CreateTempDir();
        var transitiveDir = CreateTempDir();
        try
        {
            WriteToml(transitiveDir, """name = "Transitive" """);
            File.WriteAllText(Path.Combine(transitiveDir, "trans.mist"), "// transitive source");

            // The mismatched project references "Transitive" using the new format
            var transitiveTomlPath = Path.GetFullPath(Path.Combine(transitiveDir, "mist.toml"))
                .Replace("\\", "/");
            WriteToml(mismatchedDir, $$"""
                name = "CorrectName"

                [references]
                Transitive = { path = "{{transitiveTomlPath}}" }
                """);
            File.WriteAllText(Path.Combine(mismatchedDir, "lib.mist"), "// mismatched source");

            WriteToml(rootDir, """name = "Root" """);

            // Root references the mismatched project with key "WrongName"
            var mismatchedTomlPath = Path.GetFullPath(Path.Combine(mismatchedDir, "mist.toml"));
            var references = ImmutableArray.Create(new ProjectReference("WrongName", mismatchedTomlPath));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();
            _ = Assert.Single(diags);
            Assert.Equal("MIST0070", diags[0].Number.Value);

            // Neither the mismatched project nor its transitive dep should be in the result
            Assert.Empty(result);
            var resolvedPaths = result.Select(r => r.CanonicalTomlPath).ToList();
            Assert.DoesNotContain(
                Path.GetFullPath(Path.Combine(transitiveDir, "mist.toml")),
                resolvedPaths);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
            Directory.Delete(mismatchedDir, recursive: true);
            Directory.Delete(transitiveDir, recursive: true);
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
                new ProjectReference("Bogus1", bogus1),
                new ProjectReference("Bogus2", bogus2),
                new ProjectReference("Bogus3", bogus3));

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

            Assert.True(diagnostics.HasErrors);
            var diags = diagnostics.ToImmutableArray();

            // All 3 errors should be reported, not just the first
            Assert.Equal(3, diags.Length);
            Assert.All(diags, d =>
            {
                Assert.Equal("MIST0062", d.Number.Value);
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

    // --- C# project and assembly references ---------------------------------

    private static ProjectReference DirectoryRef(string name, string directory)
    {
        return new ProjectReference(name, Path.Combine(directory, ProjectFileParser.FileName), IsDirectory: true);
    }

    private static string CreateFiles(string parent, string name, params string[] files)
    {
        var directory = Path.Combine(parent, name);
        foreach(var file in files)
        {
            var path = Path.Combine(directory, file);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.EndsWith(ProjectFileParser.FileName, StringComparison.Ordinal) ? $"name = \"{name}\"" : "");
        }

        return directory;
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ADirectoryWithOneCSharpProject_IsAnExternalReference_IgnoringItsBuildOutput()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            var bridge = CreateFiles(rootDir, "Bridge", "Bridge.csproj", "Program.cs", "bin/Debug/net10.0/Bridge.dll", "obj/Bridge.dll");

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.ResolveAll(rootDir, [DirectoryRef("Bridge", bridge)], diagnostics);

            Assert.False(diagnostics.HasErrors);
            Assert.Empty(result.Projects);
            var external = Assert.Single(result.ExternalReferences);
            Assert.Equal(
                new ExternalReference("Bridge", Path.Combine(bridge, "Bridge.csproj"), ReferenceKind.CSharpProject, Path.Combine(rootDir, ProjectFileParser.FileName)),
                external);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ADirectoryWithOneMistToml_IsAMistProjectAsBefore()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            var lib = CreateFiles(rootDir, "Lib", "mist.toml", "Lib.mist");

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.ResolveAll(rootDir, [DirectoryRef("Lib", lib)], diagnostics);

            Assert.False(diagnostics.HasErrors);
            Assert.Empty(result.ExternalReferences);
            Assert.Equal(Path.Combine(lib, ProjectFileParser.FileName), Assert.Single(result.Projects).CanonicalTomlPath);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ADirectoryWithNoCandidate_SaysWhatItLacks()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            var empty = CreateFiles(rootDir, "Empty", "README.md", "bin/Empty.dll");

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.ResolveAll(rootDir, [DirectoryRef("Empty", empty)], diagnostics);

            var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal("MIST0063", diagnostic.Number.Value);
            Assert.Equal($"Referenced directory '{empty}' holds no mist.toml, .csproj or .dll.", diagnostic.Message);
            Assert.Empty(result.Projects);
            Assert.Empty(result.ExternalReferences);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ADirectoryWithTwoCandidates_IsAmbiguousAtTheReferencingManifest()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            var both = CreateFiles(rootDir, "Both", "mist.toml", "Both.csproj");

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.ResolveAll(rootDir, [DirectoryRef("Both", both)], diagnostics);

            var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal(DriverDiagnostics.ReferenceAmbiguous.Number, diagnostic.Number);
            Assert.Equal(Path.Combine(rootDir, ProjectFileParser.FileName), diagnostic.Location.Path);
            Assert.Equal(
                $"Reference 'Both' names directory '{both}', which holds more than one referenceable file: Both.csproj, mist.toml. Name the intended file in 'path'.",
                diagnostic.Message);
            Assert.Empty(result.Projects);
            Assert.Empty(result.ExternalReferences);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AnAssemblyWhoseFileNameDiffersFromItsKey_IsANameMismatch()
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            var widgets = Path.Combine(CreateFiles(rootDir, "libs", "Widgets.dll"), "Widgets.dll");

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.ResolveAll(
                rootDir, [new ProjectReference("Gadgets", widgets, ReferenceKind.Assembly)], diagnostics);

            var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal(DriverDiagnostics.ProjectReferenceNameMismatch.Number, diagnostic.Number);
            Assert.Equal($"Reference key 'Gadgets' does not match 'Widgets', the name the assembly '{widgets}' produces.", diagnostic.Message);
            Assert.Empty(result.ExternalReferences);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData(ReferenceKind.CSharpProject, "Missing.csproj")]
    [InlineData(ReferenceKind.Assembly, "Missing.dll")]
    public void ANamedFileThatDoesNotExist_IsReportedWithItsKey(ReferenceKind kind, string fileName)
    {
        var rootDir = CreateTempDir();
        try
        {
            WriteToml(rootDir, """name = "Root" """);
            var missing = Path.Combine(rootDir, "nowhere", fileName);

            var diagnostics = new DiagnosticBag();
            _ = ProjectReferenceResolver.ResolveAll(rootDir, [new ProjectReference("Missing", missing, kind)], diagnostics);

            var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
            Assert.Equal(DriverDiagnostics.ReferenceFileNotFound.Number, diagnostic.Number);
            Assert.Equal($"Reference 'Missing' names '{missing}', which does not exist.", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AReferencedMistProjectsCSharpReferences_PrecedeTheRoots()
    {
        var rootDir = CreateTempDir();
        try
        {
            var rootBridge = Path.Combine(CreateFiles(rootDir, "RootBridge", "RootBridge.csproj"), "RootBridge.csproj");
            var libBridge = Path.Combine(CreateFiles(rootDir, "LibBridge", "LibBridge.csproj"), "LibBridge.csproj");
            var lib = CreateFiles(rootDir, "Lib", "Lib.mist");
            WriteToml(lib, """
                name = "Lib"

                [references]
                LibBridge = { path = "../LibBridge/LibBridge.csproj" }
                """);
            WriteToml(rootDir, """name = "Root" """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectReferenceResolver.ResolveAll(
                rootDir,
                [new ProjectReference("RootBridge", rootBridge, ReferenceKind.CSharpProject), DirectoryRef("Lib", lib)],
                diagnostics);

            Assert.False(diagnostics.HasErrors);
            Assert.Equal([libBridge, rootBridge], result.ExternalReferences.Select(reference => reference.Path));
            Assert.Equal(
                [Path.Combine(lib, ProjectFileParser.FileName), Path.Combine(rootDir, ProjectFileParser.FileName)],
                result.ExternalReferences.Select(reference => reference.DeclaringManifestPath));
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }
}