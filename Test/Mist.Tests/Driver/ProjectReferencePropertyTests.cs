using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for <see cref="ProjectReferenceResolver"/>.
/// All tests are pure in-process — no process spawning.
/// </summary>
public sealed class ProjectReferencePropertyTests
{
    // ── Helpers ───────────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    private static ProjectReference RefTo(string projectDir)
    {
        return new ProjectReference(
            Path.GetFullPath(Path.Combine(projectDir, ProjectFileParser.FileName)));
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests);
        foreach (var sample in samples)
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
                for (var i = 0; i < cycleLength; i++)
                {
                    dirs.Add(CreateTempDir());
                }

                // Each directory references the next; the last references the first
                for (var i = 0; i < cycleLength; i++)
                {
                    var nextDir = dirs[(i + 1) % cycleLength];
                    var nextTomlPath = Path.GetFullPath(
                        Path.Combine(nextDir, "mist.toml")).Replace("\\", "/");

                    WriteToml(dirs[i], $"""
                        name = "Project{i}"

                        [[references]]
                        path = "{nextTomlPath}"
                        """);
                }

                WriteToml(rootDir, """name = "Root" """);

                var references = ImmutableArray.Create(RefTo(dirs[0]));

                var diagnostics = new DiagnosticBag();
                ProjectReferenceResolver.Resolve(rootDir, references, diagnostics);

                Assert.True(diagnostics.HasErrors,
                    $"Cycle of length {cycleLength} should produce errors");

                var diags = diagnostics.ToImmutableArray();
                var cycleDiag = diags.FirstOrDefault(d =>
                    d.Id.Value == "MIST_PROJECT_CIRCULAR_REFERENCE");

                Assert.NotNull(cycleDiag);
                Assert.Equal(DiagnosticSeverity.Error, cycleDiag.Severity);

                // The cycle message should contain the " → " separator
                Assert.Contains("→", cycleDiag.Message);
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }
                foreach (var d in dirs)
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
                for (var i = 0; i < sharedCount; i++)
                {
                    var sharedDir = CreateTempDir();
                    sharedDirs.Add(sharedDir);
                    WriteToml(sharedDir, $"""name = "Shared{i}" """);
                    File.WriteAllText(
                        Path.Combine(sharedDir, $"shared{i}.mist"), $"// Shared{i}");
                }

                // Left references all shared
                var leftRefs = string.Join("\n",
                    sharedDirs.Select(d =>
                    {
                        var p = Path.GetFullPath(
                            Path.Combine(d, "mist.toml")).Replace("\\", "/");
                        return $"[[references]]\npath = \"{p}\"";
                    }));
                WriteToml(leftDir, $"name = \"Left\"\n\n{leftRefs}");

                // Right references all shared
                var rightRefs = string.Join("\n",
                    sharedDirs.Select(d =>
                    {
                        var p = Path.GetFullPath(
                            Path.Combine(d, "mist.toml")).Replace("\\", "/");
                        return $"[[references]]\npath = \"{p}\"";
                    }));
                WriteToml(rightDir, $"name = \"Right\"\n\n{rightRefs}");

                WriteToml(rootDir, """name = "Root" """);

                var references = ImmutableArray.Create(RefTo(leftDir), RefTo(rightDir));

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
                foreach (var d in sharedDirs)
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
                for (var i = 0; i < errorCount; i++)
                {
                    var bogusPath = Path.Combine(
                        Path.GetTempPath(),
                        $"mist_bogus_{Guid.NewGuid():N}",
                        "mist.toml");
                    refs.Add(new ProjectReference(bogusPath));
                }

                var diagnostics = new DiagnosticBag();
                ProjectReferenceResolver.Resolve(
                    rootDir, refs.ToImmutableArray(), diagnostics);

                Assert.True(diagnostics.HasErrors);
                var diags = diagnostics.ToImmutableArray();

                // Exactly N diagnostics should be reported
                Assert.Equal(errorCount, diags.Length);
                Assert.All(diags, d =>
                {
                    Assert.Equal("MIST_PROJECT_REFERENCE_NOT_FOUND", d.Id.Value);
                    Assert.Equal(DiagnosticSeverity.Error, d.Severity);
                });
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }
            }
        });
    }

    // ── CompilerDriver helpers for Property 6 and 7 ──────────────────

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the
    /// <see cref="MistCompilation"/> passed to <c>Build</c>.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();

        public MistCompilation? CapturedCompilation { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            CapturedCompilation = compilation;
            return _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
        }
    }

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
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

    // ── Property 6: Referenced unit metadata is correct ───────────────
    // Feature: project-references, Property 6: Referenced unit metadata is correct

    [Fact]
    public void P6_ReferencedUnitMetadata_IsCorrect()
    {
        var gen = GenProjectName().SelectMany(name =>
            Gen.Choose(1, 3).Select(fileCount => (name, fileCount)));

        RunProperty(gen, input =>
        {
            var (refName, fileCount) = input;
            var rootDir = CreateTempDir();
            var refDir = CreateTempDir();
            try
            {
                // Create referenced project with random name and source files
                WriteToml(refDir, $"name = \"{refName}\"\nnamespace_root = \"{refName}\"");
                for (var i = 0; i < fileCount; i++)
                {
                    File.WriteAllText(
                        Path.Combine(refDir, $"file{i}.mist"),
                        $"// source file {i}");
                }

                // Create root project referencing it
                var refTomlPath = Path.GetFullPath(
                    Path.Combine(refDir, "mist.toml")).Replace("\\", "/");
                WriteToml(rootDir, $"name = \"Root\"\nnamespace_root = \"Root\"\n\n[[references]]\npath = \"{refTomlPath}\"");
                File.WriteAllText(
                    Path.Combine(rootDir, "main.mist"), "// root source");

                var capture = new CapturingSemanticModelBuilder();
                var driver = CreateDriverWithCapture(capture);

                var sourcePaths = Directory.GetFiles(rootDir, "*.mist");
                var options = new CompilerOptions(
                    ProjectFolder: rootDir,
                    SourcePaths: sourcePaths.ToImmutableArray(),
                    Backend: new BackendId("csharp"),
                    OutputDirectory: Path.Combine(rootDir, "out"),
                    BinDirectory: Path.Combine(rootDir, "bin"),
                    ProjectName: "Root",
                    NamespaceRoot: "Root",
                    EnabledAnalyzerIds: ImmutableArray<string>.Empty);

                driver.Compile(options);

                Assert.NotNull(capture.CapturedCompilation);
                var units = capture.CapturedCompilation!.Units;

                var refTomlCanonical = Path.GetFullPath(
                    Path.Combine(refDir, "mist.toml"));
                var refUnits = units
                    .Where(u => u.OriginProjectPath == refTomlCanonical)
                    .ToList();

                Assert.Equal(fileCount, refUnits.Count);

                foreach (var unit in refUnits)
                {
                    Assert.Equal(refName, unit.NamespaceRoot);
                    Assert.Equal(refDir, unit.ProjectFolder);
                    Assert.Equal(refTomlCanonical, unit.OriginProjectPath);
                }
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }
                try { Directory.Delete(refDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 7: Topological ordering — deepest dependency first ───
    // Feature: project-references, Property 7: Topological ordering — deepest dependency first

    [Fact]
    public void P7_TopologicalOrdering_DeepestDependencyFirst()
    {
        // Generator: chain depth 2–4 (A→B→C or A→B→C→D etc.)
        var gen = Gen.Choose(2, 4);

        RunProperty(gen, depth =>
        {
            var rootDir = CreateTempDir();
            var chainDirs = new List<string>();
            try
            {
                // Create chain of projects: chain[0] is deepest, chain[depth-1] is shallowest
                for (var i = 0; i < depth; i++)
                {
                    chainDirs.Add(CreateTempDir());
                }

                // Deepest project has no references
                WriteToml(chainDirs[0], "name = \"Depth0\"\nnamespace_root = \"Depth0\"");
                File.WriteAllText(
                    Path.Combine(chainDirs[0], "d0.mist"), "// depth 0");

                // Each subsequent project references the previous one
                for (var i = 1; i < depth; i++)
                {
                    var prevToml = Path.GetFullPath(
                        Path.Combine(chainDirs[i - 1], "mist.toml")).Replace("\\", "/");
                    WriteToml(chainDirs[i], $"name = \"Depth{i}\"\nnamespace_root = \"Depth{i}\"\n\n[[references]]\npath = \"{prevToml}\"");
                    File.WriteAllText(
                        Path.Combine(chainDirs[i], $"d{i}.mist"), $"// depth {i}");
                }

                // Root references the shallowest in the chain
                var shallowToml = Path.GetFullPath(
                    Path.Combine(chainDirs[depth - 1], "mist.toml")).Replace("\\", "/");
                WriteToml(rootDir, $"name = \"Root\"\nnamespace_root = \"Root\"\n\n[[references]]\npath = \"{shallowToml}\"");
                File.WriteAllText(
                    Path.Combine(rootDir, "main.mist"), "// root source");

                var capture = new CapturingSemanticModelBuilder();
                var driver = CreateDriverWithCapture(capture);

                var sourcePaths = Directory.GetFiles(rootDir, "*.mist");
                var options = new CompilerOptions(
                    ProjectFolder: rootDir,
                    SourcePaths: sourcePaths.ToImmutableArray(),
                    Backend: new BackendId("csharp"),
                    OutputDirectory: Path.Combine(rootDir, "out"),
                    BinDirectory: Path.Combine(rootDir, "bin"),
                    ProjectName: "Root",
                    NamespaceRoot: "Root",
                    EnabledAnalyzerIds: ImmutableArray<string>.Empty);

                driver.Compile(options);

                Assert.NotNull(capture.CapturedCompilation);
                var units = capture.CapturedCompilation!.Units;

                // Verify ordering: units from deeper projects appear before shallower
                for (var i = 0; i < depth - 1; i++)
                {
                    var deeperToml = Path.GetFullPath(
                        Path.Combine(chainDirs[i], "mist.toml"));
                    var shallowerToml = Path.GetFullPath(
                        Path.Combine(chainDirs[i + 1], "mist.toml"));

                    var deeperIndices = units
                        .Select((u, idx) => (u, idx))
                        .Where(x => x.u.OriginProjectPath == deeperToml)
                        .Select(x => x.idx)
                        .ToList();

                    var shallowerIndices = units
                        .Select((u, idx) => (u, idx))
                        .Where(x => x.u.OriginProjectPath == shallowerToml)
                        .Select(x => x.idx)
                        .ToList();

                    Assert.NotEmpty(deeperIndices);
                    Assert.NotEmpty(shallowerIndices);

                    Assert.True(deeperIndices.Max() < shallowerIndices.Min(),
                        $"Depth {i} units (max index {deeperIndices.Max()}) should appear " +
                        $"before depth {i + 1} units (min index {shallowerIndices.Min()})");
                }
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }
                foreach (var d in chainDirs)
                {
                    try { Directory.Delete(d, recursive: true); } catch { }
                }
            }
        });
    }

    // ── Property 8: Transitive types are not directly visible ─────────
    // Feature: project-references, Property 8: Transitive types are not directly visible

    /// <summary>
    /// Generates a valid Mist type name: 3–8 uppercase-starting letters.
    /// </summary>
    private static Gen<string> GenTypeName()
    {
        var upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var lower = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
        return Gen.Elements(upper).SelectMany(first =>
            Gen.Choose(2, 7).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(lower), restLen)
                    .Select(rest => first + new string(rest))));
    }

    [Fact]
    public void P8_TransitiveTypes_AreNotDirectlyVisible()
    {
        var gen = GenTypeName();

        // Use a shared static driver for all iterations
        var sharedDriver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        RunProperty(gen, typeName =>
        {
            var rootDir = CreateTempDir();
            var dirB = CreateTempDir();
            var dirC = CreateTempDir();
            try
            {
                // Project C: declares a type with the random name
                WriteToml(dirC, "name = \"CLib\"\nnamespace_root = \"CLib\"");
                File.WriteAllText(
                    Path.Combine(dirC, "type.mist"),
                    $"namespace CLib;\n\npublic class {typeName}\n{{\n    public static int Value()\n    {{\n        return 1;\n    }}\n}}");

                // Project B: references C
                var cToml = Path.GetFullPath(
                    Path.Combine(dirC, "mist.toml")).Replace("\\", "/");
                WriteToml(dirB, $"name = \"BLib\"\nnamespace_root = \"BLib\"\n\n[[references]]\npath = \"{cToml}\"");
                File.WriteAllText(
                    Path.Combine(dirB, "bridge.mist"),
                    "namespace BLib;\n\npublic class Bridge\n{\n    public static int Get()\n    {\n        return 0;\n    }\n}");

                // Root: references only B, tries to use C's type
                var bToml = Path.GetFullPath(
                    Path.Combine(dirB, "mist.toml")).Replace("\\", "/");
                WriteToml(rootDir, $"name = \"Root\"\nnamespace_root = \"Root\"\n\n[[references]]\npath = \"{bToml}\"");
                File.WriteAllText(
                    Path.Combine(rootDir, "main.mist"),
                    $"namespace Root;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        var x = CLib.{typeName}.Value();\n    }}\n}}");

                var sourcePaths = Directory.GetFiles(rootDir, "*.mist");
                var options = new CompilerOptions(
                    ProjectFolder: rootDir,
                    SourcePaths: sourcePaths.ToImmutableArray(),
                    Backend: new BackendId("csharp"),
                    OutputDirectory: Path.Combine(rootDir, "out"),
                    BinDirectory: Path.Combine(rootDir, "bin"),
                    ProjectName: "Root",
                    NamespaceRoot: "Root",
                    EnabledAnalyzerIds: ImmutableArray<string>.Empty);

                var result = sharedDriver.Compile(options);

                // Compilation should fail — C's type is not directly visible
                Assert.False(result.Succeeded,
                    $"Expected compilation to fail when using transitive type CLib.{typeName}. " +
                    $"Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
            }
            finally
            {
                try { Directory.Delete(rootDir, recursive: true); } catch { }
                try { Directory.Delete(dirB, recursive: true); } catch { }
                try { Directory.Delete(dirC, recursive: true); } catch { }
            }
        });
    }
}
