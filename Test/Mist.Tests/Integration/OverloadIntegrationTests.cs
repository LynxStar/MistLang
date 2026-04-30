// Feature: method-overloading, Integration tests for overloaded method programs
// **Validates: Requirements 7.1, 7.2, 7.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end integration tests that compile Mist programs with overloaded
/// methods and constructors through the full pipeline (lex → parse → semantic
/// model → type check → emit → Roslyn compile) and verify successful compilation.
/// </summary>
public sealed class OverloadIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public OverloadIntegrationTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "MistOverloadIntegration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helper: compile a Mist source string through the full pipeline ──

    private CompilerResult CompileMistSource(string mistSource, string fileName = "Program.mist")
    {
        var sourcePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(sourcePath, mistSource);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _tempDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(_tempDir, "out"),
            BinDirectory: Path.Combine(_tempDir, "bin"),
            ProjectName: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        return driver.Compile(options);
    }

    private static string FormatDiagnostics(CompilerResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  [{d.Id.Value}] {d.Message}");
        return string.Join(Environment.NewLine, errors);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 1: Overloaded methods — same name, different parameter types
    // Validates: Requirements 7.1, 7.3
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void OverloadedMethods_FullPipeline_SuccessfulCompilation()
    {
        var source = """
            class Foo {
                public void M(int x) { }
                public void M(bool b) { }
                public void M(double d) { }

                public static void Main() { }
            }
            """;

        var result = CompileMistSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed with overloaded methods:{Environment.NewLine}{FormatDiagnostics(result)}");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 2: Overloaded constructors — same class, different parameter types
    // Validates: Requirements 7.2, 7.3
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void OverloadedConstructors_FullPipeline_SuccessfulCompilation()
    {
        var source = """
            class Foo {
                private int value;
                private bool flag;

                public Foo(int x) {
                    this.value = x;
                }

                public Foo(bool b) {
                    this.flag = b;
                }

                public static void Main() { }
            }
            """;

        var result = CompileMistSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed with overloaded constructors:{Environment.NewLine}{FormatDiagnostics(result)}");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 3: Overloaded interface methods + implementing class
    // Validates: Requirements 7.1, 7.3
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void OverloadedInterfaceMethods_FullPipeline_SuccessfulCompilation()
    {
        var source = """
            interface IProcessor {
                int Process(int x);
                bool Process(bool b);
            }

            class Processor : IProcessor {
                public int Process(int x) { return x; }
                public bool Process(bool b) { return b; }

                public static void Main() { }
            }
            """;

        var result = CompileMistSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed with overloaded interface methods:{Environment.NewLine}{FormatDiagnostics(result)}");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 4: Cross-hierarchy overloads — base class + derived class
    // Validates: Requirements 7.1, 7.3
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void CrossHierarchyOverloads_FullPipeline_SuccessfulCompilation()
    {
        var source = """
            class Base {
                public void Act(int x) { }
            }

            class Derived : Base {
                public void Act(bool b) { }

                public static void Main() { }
            }
            """;

        var result = CompileMistSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed with cross-hierarchy overloads:{Environment.NewLine}{FormatDiagnostics(result)}");
    }
}
