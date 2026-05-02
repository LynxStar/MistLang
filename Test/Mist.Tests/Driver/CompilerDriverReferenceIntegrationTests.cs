using System.Collections.Immutable;
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
/// Integration tests for <see cref="CompilerDriver"/> with project references.
/// Validates that referenced project types are visible, invalid references
/// produce correct diagnostics, and unit ordering follows topological order.
///
/// Validates: Requirements 3.1, 3.2, 5.1–5.5.
/// </summary>
public sealed class CompilerDriverReferenceIntegrationTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _scratchDir;

    public CompilerDriverReferenceIntegrationTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-ref-integ-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a project directory with a mist.toml and optional source files.
    /// Returns the absolute path to the project directory.
    /// </summary>
    private string CreateProject(
        string name,
        string? namespaceRoot = null,
        string[]? referencePaths = null,
        Dictionary<string, string>? sourceFiles = null)
    {
        var projectDir = Path.Combine(_scratchDir, name);
        Directory.CreateDirectory(projectDir);

        var toml = $"name = \"{name}\"\n";
        if (namespaceRoot is not null)
        {
            toml += $"namespace_root = \"{namespaceRoot}\"\n";
        }

        if (referencePaths is not null)
        {
            foreach (var refPath in referencePaths)
            {
                // Use forward slashes for TOML compatibility
                var escapedPath = refPath.Replace("\\", "/");
                toml += $"\n[[references]]\npath = \"{escapedPath}\"\n";
            }
        }

        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

        if (sourceFiles is not null)
        {
            foreach (var (fileName, content) in sourceFiles)
            {
                File.WriteAllText(Path.Combine(projectDir, fileName), content);
            }
        }

        return projectDir;
    }

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the
    /// <see cref="MistCompilation"/> passed to <c>Build</c>, so we can
    /// inspect <c>compilation.Units</c> for ordering assertions.
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

    // ── Test 1: Single reference — B's types visible in A ────────────

    /// <summary>
    /// Project A references project B. B declares a public class. A uses
    /// that class. The compilation should succeed without undefined-type
    /// diagnostics for B's types.
    /// </summary>
    [Fact]
    public void SingleReference_ReferencedTypesAreVisible()
    {
        // Project B: declares a public class
        var projectB = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            sourceFiles: new Dictionary<string, string>
            {
                ["Greeter.mist"] = """
                    namespace SharedLib;

                    public class Greeter
                    {
                        public static int GetValue()
                        {
                            return 42;
                        }
                    }
                    """
            });

        // Project A: references B and uses B's type
        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [projectB],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main()
                        {
                            var x = SharedLib.Greeter.GetValue();
                        }
                    }
                    """
            });

        var sourcePaths = Directory.GetFiles(projectA, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectA,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectA, "out"),
            BinDirectory: Path.Combine(projectA, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = SharedDriver.Compile(options);

        // The compilation should not have any undefined-type diagnostics
        // for the referenced project's types.
        var undefinedTypeDiags = result.Diagnostics
            .Where(d => d.Id.Value.Contains("UNDEFINED") || d.Id.Value.Contains("UNKNOWN_TYPE"))
            .ToList();

        Assert.Empty(undefinedTypeDiags);

        // No reference-related errors should be present
        var refErrors = result.Diagnostics
            .Where(d => d.Id.Value.StartsWith("MIST_PROJECT_REFERENCE"))
            .ToList();

        Assert.Empty(refErrors);
    }

    // ── Test 2: Invalid reference → MIST_PROJECT_REFERENCE_NOT_FOUND ─

    /// <summary>
    /// Project A declares a reference to a non-existent path. The
    /// compilation should abort with a <c>MIST_PROJECT_REFERENCE_NOT_FOUND</c>
    /// diagnostic.
    /// </summary>
    [Fact]
    public void InvalidReference_ProducesNotFoundDiagnostic()
    {
        var bogusPath = Path.Combine(_scratchDir, "NonExistent", "mist.toml");

        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [bogusPath],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main()
                        {
                        }
                    }
                    """
            });

        var sourcePaths = Directory.GetFiles(projectA, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectA,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectA, "out"),
            BinDirectory: Path.Combine(projectA, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = SharedDriver.Compile(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics,
            d => d.Id.Value == "MIST_PROJECT_REFERENCE_NOT_FOUND");
    }

    // ── Test 3: Ordering — C's units appear before B's units ─────────

    /// <summary>
    /// Project B references project C. Project A references project B.
    /// Units from C should appear before units from B in
    /// <c>compilation.Units</c> (deepest dependency first).
    /// </summary>
    [Fact]
    public void Ordering_DeepestDependencyUnitsAppearFirst()
    {
        // Project C: leaf dependency
        var projectC = CreateProject(
            name: "CoreLib",
            namespaceRoot: "CoreLib",
            sourceFiles: new Dictionary<string, string>
            {
                ["Core.mist"] = """
                    namespace CoreLib;

                    public class Core
                    {
                        public static int Zero()
                        {
                            return 0;
                        }
                    }
                    """
            });

        // Project B: references C
        var projectB = CreateProject(
            name: "MiddleLib",
            namespaceRoot: "MiddleLib",
            referencePaths: [projectC],
            sourceFiles: new Dictionary<string, string>
            {
                ["Middle.mist"] = """
                    namespace MiddleLib;

                    public class Middle
                    {
                        public static int GetCore()
                        {
                            return CoreLib.Core.Zero();
                        }
                    }
                    """
            });

        // Project A: references B (which transitively pulls in C)
        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [projectB],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main()
                        {
                        }
                    }
                    """
            });

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        var sourcePaths = Directory.GetFiles(projectA, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectA,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectA, "out"),
            BinDirectory: Path.Combine(projectA, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        // Find the canonical toml paths for C and B
        var cTomlPath = Path.GetFullPath(Path.Combine(projectC, "mist.toml"));
        var bTomlPath = Path.GetFullPath(Path.Combine(projectB, "mist.toml"));

        // Get indices of units from C and B (by OriginProjectPath)
        var cUnits = units
            .Select((u, i) => (Unit: u, Index: i))
            .Where(x => x.Unit.OriginProjectPath == cTomlPath)
            .ToList();

        var bUnits = units
            .Select((u, i) => (Unit: u, Index: i))
            .Where(x => x.Unit.OriginProjectPath == bTomlPath)
            .ToList();

        Assert.NotEmpty(cUnits);
        Assert.NotEmpty(bUnits);

        // All C units should appear before all B units
        var lastCIndex = cUnits.Max(x => x.Index);
        var firstBIndex = bUnits.Min(x => x.Index);

        Assert.True(lastCIndex < firstBIndex,
            $"Expected all units from C (last index {lastCIndex}) to appear before " +
            $"units from B (first index {firstBIndex}) in compilation.Units. " +
            $"Unit order: {string.Join(", ", units.Select(u => u.OriginProjectPath ?? "(user/stdlib)"))}");
    }

    // ── Test 4: Transitive reference — C's types NOT visible in A ────

    /// <summary>
    /// A→B→C: Project C declares a public class. Project B references C and
    /// uses C's type. Project A references only B and attempts to use C's
    /// type directly. A's compilation should produce a diagnostic because C
    /// is only transitively referenced (not directly referenced by A).
    ///
    /// Validates: Requirements 6.1, 6.2, 6.4.
    /// </summary>
    [Fact]
    public void TransitiveReference_TransitiveTypesNotVisibleInA()
    {
        // Project C: leaf dependency with a public class
        var projectC = CreateProject(
            name: "CoreLib",
            namespaceRoot: "CoreLib",
            sourceFiles: new Dictionary<string, string>
            {
                ["CoreType.mist"] = """
                    namespace CoreLib;

                    public class CoreType
                    {
                        public static int Value()
                        {
                            return 99;
                        }
                    }
                    """
            });

        // Project B: references C and uses C's type
        var projectB = CreateProject(
            name: "MiddleLib",
            namespaceRoot: "MiddleLib",
            referencePaths: [projectC],
            sourceFiles: new Dictionary<string, string>
            {
                ["Bridge.mist"] = """
                    namespace MiddleLib;

                    public class Bridge
                    {
                        public static int GetCoreValue()
                        {
                            return CoreLib.CoreType.Value();
                        }
                    }
                    """
            });

        // Project A: references only B, tries to use C's type directly
        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [projectB],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main()
                        {
                            var x = CoreLib.CoreType.Value();
                        }
                    }
                    """
            });

        var sourcePaths = Directory.GetFiles(projectA, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectA,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectA, "out"),
            BinDirectory: Path.Combine(projectA, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = SharedDriver.Compile(options);

        // A should fail because C's types are not directly visible
        Assert.False(result.Succeeded,
            "Compilation should fail when A uses a transitive-only type from C. " +
            $"Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        // There should be an undefined-type diagnostic for CoreLib.CoreType
        var undefinedTypeDiags = result.Diagnostics
            .Where(d => d.Id.Value.Contains("UNDEFINED"))
            .ToList();

        Assert.NotEmpty(undefinedTypeDiags);
    }

    // ── Test 5: Explicit transitive — C's types visible in both A and B

    /// <summary>
    /// A→B, A→C, B→C: Project A explicitly references both B and C.
    /// C's types should be visible in both A and B. The compilation should
    /// succeed without undefined-type diagnostics.
    ///
    /// Validates: Requirements 6.1, 6.2, 6.4.
    /// </summary>
    [Fact]
    public void ExplicitTransitive_TypesVisibleInBothAAndB()
    {
        // Project C: leaf dependency with a public class
        var projectC = CreateProject(
            name: "CoreLib",
            namespaceRoot: "CoreLib",
            sourceFiles: new Dictionary<string, string>
            {
                ["CoreType.mist"] = """
                    namespace CoreLib;

                    public class CoreType
                    {
                        public static int Value()
                        {
                            return 99;
                        }
                    }
                    """
            });

        // Project B: references C and uses C's type
        var projectB = CreateProject(
            name: "MiddleLib",
            namespaceRoot: "MiddleLib",
            referencePaths: [projectC],
            sourceFiles: new Dictionary<string, string>
            {
                ["Bridge.mist"] = """
                    namespace MiddleLib;

                    public class Bridge
                    {
                        public static int GetCoreValue()
                        {
                            return CoreLib.CoreType.Value();
                        }
                    }
                    """
            });

        // Project A: explicitly references both B and C
        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [projectB, projectC],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main()
                        {
                            var x = CoreLib.CoreType.Value();
                            var y = MiddleLib.Bridge.GetCoreValue();
                        }
                    }
                    """
            });

        var sourcePaths = Directory.GetFiles(projectA, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectA,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectA, "out"),
            BinDirectory: Path.Combine(projectA, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = SharedDriver.Compile(options);

        // No undefined-type diagnostics — C's types are directly visible
        var undefinedTypeDiags = result.Diagnostics
            .Where(d => d.Id.Value.Contains("UNDEFINED") || d.Id.Value.Contains("UNKNOWN_TYPE"))
            .ToList();

        Assert.Empty(undefinedTypeDiags);

        // No reference-related errors
        var refErrors = result.Diagnostics
            .Where(d => d.Id.Value.StartsWith("MIST_PROJECT_REFERENCE"))
            .ToList();

        Assert.Empty(refErrors);
    }
}
