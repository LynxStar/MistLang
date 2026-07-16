// Cross-assembly internal integration test
// Validates: Requirement 6.4 — internal members are rejected across the compilation boundary
// and permitted within the same compilation.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Deterministic integration tests verifying that internal members respect the
/// compilation-as-assembly boundary:
/// 1. A program referencing an internal member within the same compilation succeeds
///    (no MIST_TC_0172 diagnostic).
/// 2. A program referencing an internal member from an External_Assembly (using project
///    references) is rejected with MIST_TC_0172.
///
/// These are focused, non-random integration tests complementing the property-based
/// tests in <see cref="InternalCompilationBoundaryPropertyTests"/>.
///
/// Validates: Requirement 6.4
/// </summary>
public sealed class CrossAssemblyInternalIntegrationTests : IDisposable
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string scratchDir;

    public CrossAssemblyInternalIntegrationTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-cross-asm-internal-" + Guid.NewGuid().ToString("N"));
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

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles source in-process (Lex -> Parse -> SemanticModel -> TypeChecker)
    /// and returns all diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileInProcess(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return lexBag.ToImmutableArray()
            .AddRange(parseBag.ToImmutableArray())
            .AddRange(semBag.ToImmutableArray())
            .AddRange(tcBag.ToImmutableArray());
    }

    /// <summary>
    /// Creates a project directory with a mist.toml and source files on disk.
    /// Returns the absolute path to the project directory.
    /// </summary>
    private string CreateProject(
        string name,
        string namespaceRoot,
        string[]? referencePaths,
        Dictionary<string, string> sourceFiles)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);

        var toml = $"name = \"{name}\"\nnamespace_root = \"{namespaceRoot}\"\n";

        if(referencePaths is not null)
        {
            toml += "\n[references]\n";
            foreach(var refPath in referencePaths)
            {
                var escapedPath = refPath.Replace("\\", "/");
                var trimmedPath = refPath.TrimEnd('/', '\\');
                var refName = Path.GetFileName(trimmedPath);
                toml += $"{refName} = {{ path = \"{escapedPath}\" }}\n";
            }
        }

        File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

        foreach(var (fileName, content) in sourceFiles)
        {
            File.WriteAllText(Path.Combine(projectDir, fileName), content);
        }

        return projectDir;
    }

    /// <summary>
    /// Compiles a project using the shared CompilerDriver (file-based, for cross-assembly tests).
    /// </summary>
    private static CompilerResult CompileProject(string projectDir, string nameRoot)
    {
        var sourcePaths = Directory.GetFiles(projectDir, "*.mist");
        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: nameRoot,
            NamespaceRoot: nameRoot,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ─── Same-compilation tests (ACCEPT) ─────────────────────────────────

    /// <summary>
    /// An internal field accessed from a different class in the same compilation
    /// must NOT produce MIST_TC_0172. Requirement 6.3 guarantees that internal is
    /// visible within the same compilation.
    /// </summary>
    [Fact]
    public void InternalField_SameCompilation_IsAccepted()
    {
        const string source = @"
public class Library
{
    internal int value;
    public Library()
    {
        this.value = 42;
    }
}
public class Consumer
{
    public int Read(Library lib)
    {
        return lib.value;
    }
    public static void Main() { }
}";

        var diagnostics = CompileInProcess(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0172");
    }

    /// <summary>
    /// An internal method accessed from a different class in the same compilation
    /// must NOT produce MIST_TC_0172.
    /// </summary>
    [Fact]
    public void InternalMethod_SameCompilation_IsAccepted()
    {
        const string source = @"
public class Library
{
    internal int Compute()
    {
        return 99;
    }
    public static void Main() { }
}
public class Consumer
{
    public int Use(Library lib)
    {
        return lib.Compute();
    }
}";

        var diagnostics = CompileInProcess(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0172");
    }

    /// <summary>
    /// An internal static field accessed from a different class in the same compilation
    /// must NOT produce MIST_TC_0172.
    /// </summary>
    [Fact]
    public void InternalStaticField_SameCompilation_IsAccepted()
    {
        const string source = @"
public class Library
{
    internal static int counter;
    public static void Main() { }
}
public class Consumer
{
    public int Read()
    {
        return Library.counter;
    }
}";

        var diagnostics = CompileInProcess(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0172");
    }

    // ─── Cross-assembly tests (REJECT) ───────────────────────────────────

    /// <summary>
    /// An internal field in a referenced library project must be rejected with
    /// MIST_TC_0172 when accessed from the consumer project. Requirement 6.4
    /// mandates that internal members are hidden across the assembly boundary.
    /// </summary>
    [Fact]
    public void InternalField_CrossAssembly_IsRejected()
    {
        var libDir = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            referencePaths: null,
            sourceFiles: new Dictionary<string, string>
            {
                ["Lib.mist"] = @"
namespace SharedLib;

public class Library
{
    internal int value;
    public Library()
    {
        this.value = 42;
    }
}"
            });

        var consumerDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [libDir],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = @"
using SharedLib;

namespace MyApp;

public class Program
{
    public int Read()
    {
        var lib = new Library();
        return lib.value;
    }
    public static void Main() { }
}"
            });

        var result = CompileProject(consumerDir, "MyApp");

        Assert.Contains(result.Diagnostics, d => d.Id.Value == "MIST_TC_0172");
    }

    /// <summary>
    /// An internal method in a referenced library project must be rejected with
    /// MIST_TC_0172 when called from the consumer project.
    /// </summary>
    [Fact]
    public void InternalMethod_CrossAssembly_IsRejected()
    {
        var libDir = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            referencePaths: null,
            sourceFiles: new Dictionary<string, string>
            {
                ["Lib.mist"] = @"
namespace SharedLib;

public class Library
{
    internal int Compute()
    {
        return 99;
    }
}"
            });

        var consumerDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [libDir],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = @"
using SharedLib;

namespace MyApp;

public class Program
{
    public int Use()
    {
        var lib = new Library();
        return lib.Compute();
    }
    public static void Main() { }
}"
            });

        var result = CompileProject(consumerDir, "MyApp");

        Assert.Contains(result.Diagnostics, d => d.Id.Value == "MIST_TC_0172");
    }

    /// <summary>
    /// An internal static field in a referenced library project must be rejected with
    /// MIST_TC_0172 when accessed from the consumer project via static member access.
    /// </summary>
    [Fact]
    public void InternalStaticField_CrossAssembly_IsRejected()
    {
        var libDir = CreateProject(
            name: "SharedLib",
            namespaceRoot: "SharedLib",
            referencePaths: null,
            sourceFiles: new Dictionary<string, string>
            {
                ["Lib.mist"] = @"
namespace SharedLib;

public class Library
{
    internal static int counter;
}"
            });

        var consumerDir = CreateProject(
            name: "MyApp",
            namespaceRoot: "MyApp",
            referencePaths: [libDir],
            sourceFiles: new Dictionary<string, string>
            {
                ["Program.mist"] = @"
using SharedLib;

namespace MyApp;

public class Program
{
    public int Read()
    {
        return Library.counter;
    }
    public static void Main() { }
}"
            });

        var result = CompileProject(consumerDir, "MyApp");

        Assert.Contains(result.Diagnostics, d => d.Id.Value == "MIST_TC_0172");
    }
}