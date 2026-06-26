// Feature: refinement-types — Unit tests for C# emitter refinement type pattern matching output
// **Validates: Requirements 12.2, 14.3**

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that refinement type pattern matching emits correct C# code.
/// Refinement types are erased at runtime, so <c>x is AdultAge age</c> becomes a
/// predicate condition (e.g., <c>x >= 18</c>) with a variable binding.
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// for simple tests (no stdlib needed), and <see cref="CompilerDriver"/> for
/// tests requiring full compilation (switch expressions with discard patterns).
///
/// **Validates: Requirements 12.2, 14.3**
/// </summary>
public sealed class RefinementTypePatternEmissionTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── In-memory compilation helper (no stdlib) ────────────────────────

    private static string CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(tcErrors);

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    // ─── Full compilation helper (with stdlib) ───────────────────────────

    private static string CompileWithStdlibAndGetEmittedCSharp(string mistSource)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_emit_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            Assert.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
                $"Compilation errors: {string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"  {d.Id.Value}: {d.Message}"))}");

            Assert.NotNull(result.EmittedFiles);
            return result.EmittedFiles!.Aggregate(new StringBuilder(),
                (sb, kvp) => sb.AppendLine(kvp.Value)).ToString();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>x is AdultAge age</c> emits a predicate condition
    /// (<c>x >= 18</c>) with a variable binding (<c>int age = x;</c>).
    /// </summary>
    [Fact]
    public void IsRefinementType_SimpleRelational_EmitsPredicateWithBinding()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    int x = 25;
                    if (x is AdultAge age) {
                        int y = age;
                    }
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the predicate condition instead of `x is int age`
        Assert.Contains("x >= 18", output);
        // Should emit the variable binding inside the then-branch
        Assert.Contains("int age = x;", output);
        // Should NOT emit `is AdultAge` or `is int` pattern
        Assert.DoesNotContain("is AdultAge", output);
    }

    /// <summary>
    /// Verifies that <c>x is Percentage p</c> with a compound predicate
    /// emits combined conditions with <c>&amp;&amp;</c>.
    /// </summary>
    [Fact]
    public void IsRefinementType_CompoundPredicate_EmitsCombinedConditions()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main(string[] args) {
                    int x = 50;
                    if (x is Percentage p) {
                        int y = p;
                    }
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit combined conditions
        Assert.Contains("x >= 0", output);
        Assert.Contains("x <= 100", output);
        Assert.Contains("&&", output);
        // Should emit the variable binding
        Assert.Contains("int p = x;", output);
    }

    /// <summary>
    /// Verifies that <c>x is AdultAge</c> (bare, no variable) emits only
    /// the predicate condition without a variable binding.
    /// </summary>
    [Fact]
    public void IsRefinementType_BarePattern_EmitsPredicateOnly()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    int x = 25;
                    if (x is AdultAge) {
                        int y = 1;
                    }
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the predicate condition
        Assert.Contains("x >= 18", output);
        // Should NOT emit a variable binding for bare pattern
        Assert.DoesNotContain("int _ = x;", output);
    }

    /// <summary>
    /// Verifies that switch expression arms with refinement type patterns
    /// emit as <c>var varName when predicate</c> guards.
    /// </summary>
    [Fact]
    public void SwitchExpression_RefinementTypePattern_EmitsWhenGuard()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    int age = 25;
                    string result = age switch {
                        AdultAge a => "adult",
                        var _ => "unknown"
                    };
                }
            }
            """;

        var output = CompileWithStdlibAndGetEmittedCSharp(source);

        // Should emit as `var a when a >= 18`
        Assert.Contains("var a", output);
        Assert.Contains("when", output);
        Assert.Contains("a >= 18", output);
    }

    /// <summary>
    /// Verifies that switch expression arms with compound refinement type patterns
    /// emit combined guard conditions.
    /// </summary>
    [Fact]
    public void SwitchExpression_CompoundRefinementPattern_EmitsCombinedGuard()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main(string[] args) {
                    int x = 50;
                    string result = x switch {
                        Percentage p => "valid",
                        var _ => "invalid"
                    };
                }
            }
            """;

        var output = CompileWithStdlibAndGetEmittedCSharp(source);

        // Should emit combined guard conditions
        Assert.Contains("var p", output);
        Assert.Contains("when", output);
        Assert.Contains("p >= 0", output);
        Assert.Contains("p <= 100", output);
    }
}