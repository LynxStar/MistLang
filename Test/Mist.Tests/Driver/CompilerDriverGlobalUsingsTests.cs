using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Unit tests verifying that <see cref="CompilerDriver"/> correctly wires
/// global usings onto <see cref="CompilationUnit"/> instances:
/// - User units receive the root project's global usings (from mist.toml).
/// - Referenced project units receive their own project's global usings.
/// - Stdlib units keep empty global usings.
/// - <see cref="CompilerOptions.GlobalUsings"/> is applied to user units
///   when no mist.toml is present (the test/in-process path).
///
/// Validates: Requirements 3.3, 3.4, 5.1–5.5.
/// </summary>
public sealed class CompilerDriverGlobalUsingsTests : IDisposable
{
    private readonly string scratchDir;

    public CompilerDriverGlobalUsingsTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-gu-driver-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the
    /// <see cref="MistCompilation"/> passed to <c>Build</c>, so we can
    /// inspect <c>compilation.Units</c> for GlobalUsings assertions.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

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
            return inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
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
    /// Creates a project directory with a mist.toml and optional source files.
    /// Returns the absolute path to the project directory.
    /// </summary>
    private string CreateProject(
        string name,
        string? namespaceRoot = null,
        string[]? referencePaths = null,
        string[]? globalUsings = null,
        Dictionary<string, string>? sourceFiles = null)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);

        var toml = $"name = \"{name}\"\n";
        if(namespaceRoot is not null)
        {
            toml += $"namespace_root = \"{namespaceRoot}\"\n";
        }

        if(globalUsings is not null && globalUsings.Length > 0)
        {
            toml += "\n[global_usings]\nnamespaces = [";
            toml += string.Join(", ", globalUsings.Select(ns => $"\"{ns}\""));
            toml += "]\n";
        }

        if(referencePaths is not null)
        {
            toml += "\n[references]\n";
            for(var i = 0; i < referencePaths.Length; i++)
            {
                var escapedPath = referencePaths[i].Replace("\\", "/");
                // Derive a reference name from the directory name
                // If the path ends with mist.toml, use the parent directory name
                var trimmedPath = referencePaths[i].TrimEnd('/', '\\');
                var fileName = Path.GetFileName(trimmedPath);
                var refName = string.Equals(fileName, "mist.toml", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(Path.GetDirectoryName(trimmedPath)!)
                    : fileName;
                toml += $"{refName} = {{ path = \"{escapedPath}\" }}\n";
            }
        }

        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

        if(sourceFiles is not null)
        {
            foreach(var (fileName, content) in sourceFiles)
            {
                var filePath = Path.Combine(projectDir, fileName);
                var dir = Path.GetDirectoryName(filePath)!;
                _ = Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, content);
            }
        }

        return projectDir;
    }

    // ── Test 1: User units receive root project's global usings ──────

    /// <summary>
    /// When a mist.toml declares [global_usings], the driver sets
    /// GlobalUsings on all user compilation units to match.
    /// </summary>
    [Fact]
    public void UserUnits_ReceiveRootProjectGlobalUsings()
    {
        var projectDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            globalUsings: ["System", "MyApp.Models"],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main() { }
                    }
                    """
            });

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        var sourcePaths = Directory.GetFiles(projectDir, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        _ = driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        // Find user units (not stdlib, no OriginProjectPath)
        var userUnits = units.Where(u => !u.IsStdlib && u.OriginProjectPath is null).ToList();
        Assert.NotEmpty(userUnits);

        foreach(var unit in userUnits)
        {
            Assert.Equal(2, unit.GlobalUsings.Length);
            Assert.Equal("System", unit.GlobalUsings[0]);
            Assert.Equal("MyApp.Models", unit.GlobalUsings[1]);
        }
    }

    // ── Test 2: Referenced project units receive their own global usings

    /// <summary>
    /// When project A references project B, and B declares its own
    /// [global_usings], B's compilation units receive B's global usings
    /// (not A's).
    /// </summary>
    [Fact]
    public void ReferencedProjectUnits_ReceiveOwnProjectGlobalUsings()
    {
        // Project B: declares its own global usings
        var projectB = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            globalUsings: ["SharedLib.Internal"],
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

        // Project A: references B, declares different global usings
        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            globalUsings: ["System", "MyApp.Models"],
            referencePaths: [projectB],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main() { }
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

        _ = driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        var bTomlPath = Path.GetFullPath(Path.Combine(projectB, "mist.toml"));

        // Find referenced project units (from B)
        var refUnits = units.Where(u => u.OriginProjectPath == bTomlPath).ToList();
        Assert.NotEmpty(refUnits);

        // B's units should have B's global usings, not A's
        foreach(var unit in refUnits)
        {
            _ = Assert.Single(unit.GlobalUsings);
            Assert.Equal("SharedLib.Internal", unit.GlobalUsings[0]);
        }

        // User units (A's) should have A's global usings
        var userUnits = units.Where(u => !u.IsStdlib && u.OriginProjectPath is null).ToList();
        Assert.NotEmpty(userUnits);

        foreach(var unit in userUnits)
        {
            Assert.Equal(2, unit.GlobalUsings.Length);
            Assert.Equal("System", unit.GlobalUsings[0]);
            Assert.Equal("MyApp.Models", unit.GlobalUsings[1]);
        }
    }

    // ── Test 3: Stdlib units have empty global usings ────────────────

    /// <summary>
    /// Stdlib compilation units always have empty GlobalUsings regardless
    /// of what the root project declares.
    /// </summary>
    [Fact]
    public void StdlibUnits_HaveEmptyGlobalUsings()
    {
        var projectDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            globalUsings: ["System", "MyApp.Models"],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main() { }
                    }
                    """
            });

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        var sourcePaths = Directory.GetFiles(projectDir, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        _ = driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        // Find stdlib units
        var stdlibUnits = units.Where(u => u.IsStdlib).ToList();
        Assert.NotEmpty(stdlibUnits);

        foreach(var unit in stdlibUnits)
        {
            Assert.True(unit.GlobalUsings.IsEmpty,
                $"Stdlib unit '{unit.Source.Path}' should have empty GlobalUsings " +
                $"but had: [{string.Join(", ", unit.GlobalUsings)}]");
        }
    }

    // ── Test 4: CompilerOptions.GlobalUsings applied to user units ───

    /// <summary>
    /// When no mist.toml exists, <see cref="CompilerOptions.GlobalUsings"/>
    /// is applied to user compilation units (the in-process test path).
    /// </summary>
    [Fact]
    public void CompilerOptionsGlobalUsings_AppliedToUserUnits()
    {
        // Create a project directory WITHOUT a mist.toml
        var projectDir = Path.Combine(scratchDir, "NoToml");
        _ = Directory.CreateDirectory(projectDir);

        var userSource = """
            namespace TestNs;

            public class Foo
            {
                public static void Main() { }
            }
            """;
        var userPath = Path.Combine(projectDir, "Program.mist");
        File.WriteAllText(userPath, userSource);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "TestNs",
            NamespaceRoot: "TestNs",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            GlobalUsings: ImmutableArray.Create("System", "System.Collections.Generic"));

        _ = driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        // Find user units
        var userUnits = units.Where(u => !u.IsStdlib && u.OriginProjectPath is null).ToList();
        Assert.NotEmpty(userUnits);

        foreach(var unit in userUnits)
        {
            Assert.Equal(2, unit.GlobalUsings.Length);
            Assert.Equal("System", unit.GlobalUsings[0]);
            Assert.Equal("System.Collections.Generic", unit.GlobalUsings[1]);
        }

        // Stdlib units should still have empty global usings
        var stdlibUnits = units.Where(u => u.IsStdlib).ToList();
        foreach(var unit in stdlibUnits)
        {
            Assert.True(unit.GlobalUsings.IsEmpty,
                "Stdlib units should not receive CompilerOptions.GlobalUsings");
        }
    }

    // ── Test 5: mist.toml overrides CompilerOptions.GlobalUsings ─────

    /// <summary>
    /// When a mist.toml exists AND CompilerOptions.GlobalUsings is set,
    /// the mist.toml values take precedence for user units.
    /// </summary>
    [Fact]
    public void MistToml_OverridesCompilerOptionsGlobalUsings()
    {
        var projectDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            globalUsings: ["FromToml.Namespace"],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main() { }
                    }
                    """
            });

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        var sourcePaths = Directory.GetFiles(projectDir, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "MyApp",
            NamespaceRoot: "MyApp",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            GlobalUsings: ImmutableArray.Create("FromOptions.Namespace"));

        _ = driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        // User units should have the mist.toml value, not CompilerOptions
        var userUnits = units.Where(u => !u.IsStdlib && u.OriginProjectPath is null).ToList();
        Assert.NotEmpty(userUnits);

        foreach(var unit in userUnits)
        {
            _ = Assert.Single(unit.GlobalUsings);
            Assert.Equal("FromToml.Namespace", unit.GlobalUsings[0]);
        }
    }

    // ── Test 6: Referenced project without global usings → empty ──────

    /// <summary>
    /// When a referenced project has no [global_usings] section in its
    /// mist.toml, its compilation units have empty GlobalUsings.
    /// </summary>
    [Fact]
    public void ReferencedProjectWithoutGlobalUsings_HasEmptyGlobalUsings()
    {
        // Project B: no global usings declared
        var projectB = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            globalUsings: null,
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

        // Project A: references B, declares global usings
        var projectA = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            globalUsings: ["System"],
            referencePaths: [projectB],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = """
                    namespace MyApp;

                    public class Program
                    {
                        public static void Main() { }
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

        _ = driver.Compile(options);

        Assert.NotNull(capture.CapturedCompilation);
        var units = capture.CapturedCompilation!.Units;

        var bTomlPath = Path.GetFullPath(Path.Combine(projectB, "mist.toml"));

        // B's units should have empty global usings
        var refUnits = units.Where(u => u.OriginProjectPath == bTomlPath).ToList();
        Assert.NotEmpty(refUnits);

        foreach(var unit in refUnits)
        {
            Assert.True(unit.GlobalUsings.IsEmpty,
                $"Referenced project unit '{unit.Source.Path}' should have empty GlobalUsings " +
                $"when no [global_usings] section is declared");
        }

        // A's user units should still have A's global usings
        var userUnits = units.Where(u => !u.IsStdlib && u.OriginProjectPath is null).ToList();
        foreach(var unit in userUnits)
        {
            _ = Assert.Single(unit.GlobalUsings);
            Assert.Equal("System", unit.GlobalUsings[0]);
        }
    }
}