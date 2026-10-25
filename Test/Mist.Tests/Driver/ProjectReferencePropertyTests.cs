using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for <see cref="ProjectReferenceResolver"/>.
/// All tests are pure in-process — no process spawning.
/// </summary>
[Trait("Tier", "2")]
public sealed class ProjectReferencePropertyTests
{
    // ── Helpers ───────────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    private static ProjectReference RefTo(string name, string projectDir)
    {
        var path = Path.GetFullPath(Path.Combine(projectDir, ProjectFileParser.FileName));
        return new ProjectReference(name, path);
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests);
        foreach(var sample in samples)
        {
            check(sample);
        }
    }

    // ── Property 4: Cycle detection for cycles of any length ─────────
    // Feature: project-references, Property 4: Cycle detection for cycles of any length

    [Fact]
    public void P4_CycleDetection_CyclesOfAnyLength()
    {
        // Generator: cycle length N from 1 to 5
        var gen = Gen.Choose(1, 5);

        RunProperty(gen, cycleLength =>
        {
            var rootDir = CreateTempDir();
            var dirs = new List<string>();
            try
            {
                // Create N directories forming a cycle
                for(var i = 0; i < cycleLength; i++)
                {
                    dirs.Add(CreateTempDir());
                }

                // Each directory references the next; the last references the first
                for(var i = 0; i < cycleLength; i++)
                {
                    var nextIdx = (i + 1) % cycleLength;
                    var nextDir = dirs[nextIdx];
                    var nextTomlPath = Path.GetFullPath(
                        Path.Combine(nextDir, "mist.toml")).Replace("\\", "/");

                    WriteToml(dirs[i], $$"""
                        name = "Project{{i}}"

                        [references]
                        Project{{nextIdx}} = { path = "{{nextTomlPath}}" }
                        """);
                }

                WriteToml(rootDir, """name = "Root" """);

                var references = ImmutableArray.Create(RefTo("Project0", dirs[0]));

                var diagnostics = new DiagnosticBag();
                _ = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

                Assert.True(diagnostics.HasErrors,
                    $"Cycle of length {cycleLength} should produce errors");

                var diags = diagnostics.ToImmutableArray();
                var cycleDiag = diags.FirstOrDefault(d =>
                    d.Number.Value == "MIST0064");

                Assert.NotNull(cycleDiag);
                Assert.Equal(DiagnosticSeverity.Error, cycleDiag.Severity);

                // The cycle message should contain the " → " separator
                Assert.Contains("→", cycleDiag.Message);
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }

                foreach(var d in dirs)
                {
                    try { Directory.Delete(d, recursive: true); } catch { }
                }
            }
        });
    }

    // ── Property 5: Deduplication — each project parsed exactly once ──
    // Feature: project-references, Property 5: Deduplication — each project parsed exactly once

    [Fact]
    public void P5_Deduplication_EachProjectParsedExactlyOnce()
    {
        // Generator: number of shared dependencies (1–3) in a diamond graph
        var gen = Gen.Choose(1, 3);

        RunProperty(gen, sharedCount =>
        {
            var rootDir = CreateTempDir();
            var leftDir = CreateTempDir();
            var rightDir = CreateTempDir();
            var sharedDirs = new List<string>();
            try
            {
                // Create shared dependencies (leaf nodes)
                for(var i = 0; i < sharedCount; i++)
                {
                    var sharedDir = CreateTempDir();
                    sharedDirs.Add(sharedDir);
                    WriteToml(sharedDir, $"""name = "Shared{i}" """);
                    File.WriteAllText(
                        Path.Combine(sharedDir, $"shared{i}.mist"), $"// Shared{i}");
                }

                // Left references all shared
                var leftRefs = string.Join("\n",
                    sharedDirs.Select((d, i) =>
                    {
                        var p = Path.GetFullPath(
                            Path.Combine(d, "mist.toml")).Replace("\\", "/");
                        return $"Shared{i} = {{ path = \"{p}\" }}";
                    }));
                WriteToml(leftDir, $"name = \"Left\"\n\n[references]\n{leftRefs}");

                // Right references all shared
                var rightRefs = string.Join("\n",
                    sharedDirs.Select((d, i) =>
                    {
                        var p = Path.GetFullPath(
                            Path.Combine(d, "mist.toml")).Replace("\\", "/");
                        return $"Shared{i} = {{ path = \"{p}\" }}";
                    }));
                WriteToml(rightDir, $"name = \"Right\"\n\n[references]\n{rightRefs}");

                WriteToml(rootDir, """name = "Root" """);

                var references = ImmutableArray.Create(RefTo("Left", leftDir), RefTo("Right", rightDir));

                var diagnostics = new DiagnosticBag();
                var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

                Assert.False(diagnostics.HasErrors,
                    $"Diamond with {sharedCount} shared deps should not produce errors. " +
                    $"Diagnostics: {string.Join("; ", diagnostics.ToImmutableArray().Select(d => d.Message))}");

                // Each project's canonical path should appear exactly once
                var canonicalPaths = result.Select(r => r.CanonicalTomlPath).ToList();
                Assert.Equal(canonicalPaths.Count, canonicalPaths.Distinct().Count());

                // Expected: sharedCount shared + left + right = sharedCount + 2
                Assert.Equal(sharedCount + 2, result.Length);
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }

                try { Directory.Delete(leftDir, recursive: true); } catch { }

                try { Directory.Delete(rightDir, recursive: true); } catch { }

                foreach(var d in sharedDirs)
                {
                    try { Directory.Delete(d, recursive: true); } catch { }
                }
            }
        });
    }

    // ── Property 9: All independent reference errors are reported ─────
    // Feature: project-references, Property 9: All independent reference errors are reported

    [Fact]
    public void P9_AllIndependentErrors_AreReported()
    {
        // Generator: N (2–5) independent non-existent reference paths
        var gen = Gen.Choose(2, 5);

        RunProperty(gen, errorCount =>
        {
            var rootDir = CreateTempDir();
            try
            {
                WriteToml(rootDir, """name = "Root" """);

                // Create N independent non-existent paths
                var refs = new List<ProjectReference>();
                for(var i = 0; i < errorCount; i++)
                {
                    var bogusPath = Path.Combine(
                        Path.GetTempPath(),
                        $"mist_bogus_{Guid.NewGuid():N}",
                        "mist.toml");
                    refs.Add(new ProjectReference($"Bogus{i}", bogusPath));
                }

                var diagnostics = new DiagnosticBag();
                _ = ProjectReferenceResolver.Resolve(
                    rootDir, refs.ToImmutableArray(), diagnostics);

                Assert.True(diagnostics.HasErrors);
                var diags = diagnostics.ToImmutableArray();

                // Exactly N diagnostics should be reported
                Assert.Equal(errorCount, diags.Length);
                Assert.All(diags, d =>
                {
                    Assert.Equal("MIST0062", d.Number.Value);
                    Assert.Equal(DiagnosticSeverity.Error, d.Severity);
                });
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }
            }
        });
    }

    /// <summary>
    /// Generates a valid project name: 3–10 lowercase letters.
    /// </summary>
    private static Gen<string> GenProjectName()
    {
        var letters = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
        return Gen.Choose(3, 10).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(letters), len)
                .Select(chars => new string(chars)));
    }

    // ── Property 2 (named-references): Name-mismatch detection ─────────
    // Feature: named-references, Property 2: Name-mismatch detection
    // **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

    [Fact]
    public void P2_NameMismatch_ReportsExactlyOneDiagnosticAndExcludesFromResult()
    {
        // Generator: two distinct valid project names (3–10 lowercase letters)
        var gen = GenProjectName().SelectMany(actualName =>
            GenProjectName()
                .Where(refKey => refKey != actualName)
                .Select(refKey => (actualName, refKey)));

        RunProperty(gen, input =>
        {
            var (actualName, refKey) = input;
            var rootDir = CreateTempDir();
            var targetDir = CreateTempDir();
            try
            {
                // Create target project with its actual name
                WriteToml(targetDir, $"""
                    name = "{actualName}"
                    namespace_root = "{actualName}"
                    """);
                File.WriteAllText(
                    Path.Combine(targetDir, "lib.mist"), $"// {actualName} source");

                // Create root project with a reference using the WRONG key
                var targetTomlPath = Path.GetFullPath(
                    Path.Combine(targetDir, "mist.toml")).Replace("\\", "/");
                WriteToml(rootDir, $$"""
                    name = "Root"

                    [references]
                    {{refKey}} = { path = "{{targetTomlPath}}" }
                    """);

                var references = ImmutableArray.Create(
                    new ProjectReference(refKey, Path.GetFullPath(
                        Path.Combine(targetDir, ProjectFileParser.FileName))));

                var diagnostics = new DiagnosticBag();
                var result = ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

                // Assert exactly one ProjectReferenceNameMismatch diagnostic
                Assert.True(diagnostics.HasErrors,
                    $"Expected name-mismatch error for refKey='{refKey}' vs actualName='{actualName}'");

                var diags = diagnostics.ToImmutableArray();
                var mismatchDiags = diags.Where(d =>
                    d.Number.Value == "MIST0070").ToList();

                _ = Assert.Single(mismatchDiags);
                Assert.Equal(DiagnosticSeverity.Error, mismatchDiags[0].Severity);

                // Diagnostic message should contain both the declared key and actual name
                Assert.Contains(refKey, mismatchDiags[0].Message);
                Assert.Contains(actualName, mismatchDiags[0].Message);

                // The mismatched reference should NOT be in the resolved output
                var targetCanonical = Path.GetFullPath(
                    Path.Combine(targetDir, "mist.toml"));
                Assert.DoesNotContain(result,
                    r => r.CanonicalTomlPath == targetCanonical);
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }

                try { Directory.Delete(targetDir, recursive: true); } catch { }
            }
        });
    }
}