// Task 7.1: Unit tests for MistContext.g.cs conditional generation
// Validates: Requirements 12.1, 12.9

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that MistContext.g.cs is conditionally generated:
/// - Generated when any compilation unit uses structured context (Req 12.9)
/// - NOT generated when no structured context is used (Req 12.9)
/// - Contains the expected runtime infrastructure (Req 12.1)
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
/// </summary>
public sealed class MistContextGenerationTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();
    private static readonly ContextCheckerAnalyzer ContextChecker = new();

    private static Dictionary<string, string> CompileToFiles(string mistSource)
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

        // Context checker
        var ctxBag = new DiagnosticBag();
        ContextChecker.Analyze(model, ctxBag);

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        return files;
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void MistContextFile_Generated_WhenMethodHasWithClause()
    {
        // A method with a with-clause should trigger MistContext.g.cs generation.
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    Consume();
                }
                void Consume() with Config ctx {
                }
            }
            """;

        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("MistContext.g.cs"),
            "MistContext.g.cs should be generated when a method has a with-clause");
    }

    [Fact]
    public void MistContextFile_Generated_WhenWithVarDeclarationUsed()
    {
        // A with var declaration should trigger MistContext.g.cs generation.
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                }
            }
            """;

        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("MistContext.g.cs"),
            "MistContext.g.cs should be generated when with var declaration is used");
    }

    [Fact]
    public void MistContextFile_Generated_WhenWithBlockUsed()
    {
        // A with(expr) { } block should trigger MistContext.g.cs generation.
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with(new Config()) {
                    }
                }
            }
            """;

        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("MistContext.g.cs"),
            "MistContext.g.cs should be generated when with block is used");
    }

    [Fact]
    public void MistContextFile_NotGenerated_WhenNoContextUsed()
    {
        // A program with no structured context should NOT generate MistContext.g.cs.
        var source = """
            class Program {
                public static void Main() {
                    var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);

        Assert.False(files.ContainsKey("MistContext.g.cs"),
            "MistContext.g.cs should NOT be generated when no structured context is used");
    }

    [Fact]
    public void MistContextFile_ContainsExpectedInfrastructure()
    {
        // Verify the generated file contains the expected runtime infrastructure.
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                }
            }
            """;

        var files = CompileToFiles(source);

        Assert.True(files.ContainsKey("MistContext.g.cs"));
        var content = files["MistContext.g.cs"];

        // Verify key elements of the runtime helper
        Assert.Contains("internal static class MistContext", content);
        Assert.Contains("Dictionary<Type, Stack<object>>", content);
        Assert.Contains("public static void Push<T>(object value)", content);
        Assert.Contains("public static void Pop<T>()", content);
        Assert.Contains("public static T Peek<T>()", content);
    }
}