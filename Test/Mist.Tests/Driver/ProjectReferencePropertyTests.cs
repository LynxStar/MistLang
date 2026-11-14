using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class ProjectReferencePropertyTests
{
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

    [Fact]
    public void P4_CycleDetection_CyclesOfAnyLength()
    {
        var gen = Gen.Choose(1, 5);

        RunProperty(gen, cycleLength =>
        {
            var rootDir = CreateTempDir();
            var dirs = new List<string>();
            try
            {
                for(var i = 0; i < cycleLength; i++)
                {
                    dirs.Add(CreateTempDir());
                }

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

    [Fact]
    public void P5_Deduplication_EachProjectParsedExactlyOnce()
    {
        var gen = Gen.Choose(1, 3);

        RunProperty(gen, sharedCount =>
        {
            var rootDir = CreateTempDir();
            var leftDir = CreateTempDir();
            var rightDir = CreateTempDir();
            var sharedDirs = new List<string>();
            try
            {
                for(var i = 0; i < sharedCount; i++)
                {
                    var sharedDir = CreateTempDir();
                    sharedDirs.Add(sharedDir);
                    WriteToml(sharedDir, $"""name = "Shared{i}" """);
                    File.WriteAllText(
                        Path.Combine(sharedDir, $"shared{i}.mist"), $"// Shared{i}");
                }

                var leftRefs = string.Join("\n",
                    sharedDirs.Select((d, i) =>
                    {
                        var p = Path.GetFullPath(
                            Path.Combine(d, "mist.toml")).Replace("\\", "/");
                        return $"Shared{i} = {{ path = \"{p}\" }}";
                    }));
                WriteToml(leftDir, $"name = \"Left\"\n\n[references]\n{leftRefs}");

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

    [Fact]
    public void P9_AllIndependentErrors_AreReported()
    {
        var gen = Gen.Choose(2, 5);

        RunProperty(gen, errorCount =>
        {
            var rootDir = CreateTempDir();
            try
            {
                WriteToml(rootDir, """name = "Root" """);

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

    private static Gen<string> GenProjectName()
    {
        var letters = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
        return Gen.Choose(3, 10).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(letters), len)
                .Select(chars => new string(chars)));
    }

    [Fact]
    public void P2_NameMismatch_ReportsExactlyOneDiagnosticAndExcludesFromResult()
    {
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
                WriteToml(targetDir, $"""
                    name = "{actualName}"
                    namespace_root = "{actualName}"
                    """);
                File.WriteAllText(
                    Path.Combine(targetDir, "lib.mist"), $"// {actualName} source");

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

                Assert.True(diagnostics.HasErrors,
                    $"Expected name-mismatch error for refKey='{refKey}' vs actualName='{actualName}'");

                var diags = diagnostics.ToImmutableArray();
                var mismatchDiags = diags.Where(d =>
                    d.Number.Value == "MIST0070").ToList();

                _ = Assert.Single(mismatchDiags);
                Assert.Equal(DiagnosticSeverity.Error, mismatchDiags[0].Severity);

                Assert.Contains(refKey, mismatchDiags[0].Message);
                Assert.Contains(actualName, mismatchDiags[0].Message);

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