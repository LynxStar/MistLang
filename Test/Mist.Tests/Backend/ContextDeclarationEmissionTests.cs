// Task 7.2: Unit tests for context declaration statement emission
// Validates: Requirements 12.2, 12.3, 12.4, 12.5, 12.6

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
/// Unit tests verifying that context declaration statements emit correct C# code:
/// - with var x = expr; → var x = expr; MistContext.Push&lt;T&gt;(x); try { ... } finally { MistContext.Pop&lt;T&gt;(); }
/// - with TypeReference x = expr; → same pattern with explicit type
/// - with TypeReference _ = expr; → MistContext.Push&lt;T&gt;(expr); try { ... } finally { MistContext.Pop&lt;T&gt;(); }
/// - with expr; → MistContext.Push&lt;T&gt;(expr); try { ... } finally { MistContext.Pop&lt;T&gt;(); }
/// - Multiple declarations nest try/finally blocks (reverse pop order)
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
/// </summary>
public sealed class ContextDeclarationEmissionTests
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

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);

        var ctxBag = new DiagnosticBag();
        ContextChecker.Analyze(model, ctxBag);

        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        return files;
    }

    private static string GetMainFileContent(Dictionary<string, string> files)
    {
        Assert.True(files.ContainsKey("test.cs"), "Expected test.cs in emitted files");
        return files["test.cs"];
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── with var x = expr; ─────────────────────────────────────────────

    [Fact]
    public void WithVarDeclaration_EmitsVarDeclaration_PushTryFinally()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("var cfg = new Config();", content);
        Assert.Contains("MistContext.Push<Config>(cfg);", content);
        Assert.Contains("try", content);
        Assert.Contains("finally", content);
        Assert.Contains("MistContext.Pop<Config>();", content);
    }

    [Fact]
    public void WithVarDeclaration_WrapsRemainingStatementsInTry()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                    var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        // The var x = 42 should appear inside the try block (after Push, before finally)
        var pushIndex = content.IndexOf("MistContext.Push<Config>(cfg);");
        var tryIndex = content.IndexOf("try", pushIndex);
        var varXIndex = content.IndexOf("var x = 42;", tryIndex);
        var finallyIndex = content.IndexOf("finally", tryIndex);

        Assert.True(pushIndex >= 0, "Push should be emitted");
        Assert.True(tryIndex > pushIndex, "try should come after Push");
        Assert.True(varXIndex > tryIndex, "var x = 42 should be inside try block");
        Assert.True(finallyIndex > varXIndex, "finally should come after the statement");
    }

    // ─── with TypeReference x = expr; ───────────────────────────────────

    [Fact]
    public void WithTypedDeclaration_EmitsTypedVarDeclaration_PushTryFinally()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with Config cfg = new Config();
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("Config cfg = new Config();", content);
        Assert.Contains("MistContext.Push<Config>(cfg);", content);
        Assert.Contains("try", content);
        Assert.Contains("finally", content);
        Assert.Contains("MistContext.Pop<Config>();", content);
    }

    // ─── with TypeReference _ = expr; ───────────────────────────────────

    [Fact]
    public void WithDiscardDeclaration_EmitsPushWithoutLocalVariable()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with Config _ = new Config();
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        // Should NOT have a local variable declaration for the discard
        Assert.DoesNotContain("Config _ =", content);
        Assert.DoesNotContain("var _ =", content);
        // Should push the expression directly
        Assert.Contains("MistContext.Push<Config>(new Config());", content);
        Assert.Contains("try", content);
        Assert.Contains("finally", content);
        Assert.Contains("MistContext.Pop<Config>();", content);
    }

    // ─── with expr; ─────────────────────────────────────────────────────

    [Fact]
    public void WithPushStatement_EmitsPushWithInferredType()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with new Config();
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("MistContext.Push<Config>(new Config());", content);
        Assert.Contains("try", content);
        Assert.Contains("finally", content);
        Assert.Contains("MistContext.Pop<Config>();", content);
    }

    // ─── Multiple declarations nest try/finally ─────────────────────────

    [Fact]
    public void MultipleDeclarations_NestTryFinallyBlocks_ReversePopOrder()
    {
        var source = """
            class Config { }
            class Logger { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                    with var log = new Logger();
                    var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        // Both pushes should be present
        Assert.Contains("MistContext.Push<Config>(cfg);", content);
        Assert.Contains("MistContext.Push<Logger>(log);", content);

        // Both pops should be present
        Assert.Contains("MistContext.Pop<Config>();", content);
        Assert.Contains("MistContext.Pop<Logger>();", content);

        // The Logger push should be nested inside the Config's try block.
        // The order should be:
        // 1. var cfg = new Config();
        // 2. MistContext.Push<Config>(cfg);
        // 3. try {
        // 4.   var log = new Logger();
        // 5.   MistContext.Push<Logger>(log);
        // 6.   try {
        // 7.     var x = 42;
        // 8.   } finally { MistContext.Pop<Logger>(); }
        // 9. } finally { MistContext.Pop<Config>(); }

        var pushConfigIdx = content.IndexOf("MistContext.Push<Config>(cfg);");
        var pushLoggerIdx = content.IndexOf("MistContext.Push<Logger>(log);");
        var popLoggerIdx = content.IndexOf("MistContext.Pop<Logger>();");
        var popConfigIdx = content.IndexOf("MistContext.Pop<Config>();");

        Assert.True(pushConfigIdx < pushLoggerIdx, "Config push should come before Logger push");
        Assert.True(pushLoggerIdx < popLoggerIdx, "Logger push should come before Logger pop");
        Assert.True(popLoggerIdx < popConfigIdx, "Logger pop should come before Config pop (reverse order)");
    }

    // ─── Primitive type context ─────────────────────────────────────────

    [Fact]
    public void WithVarDeclaration_PrimitiveType_UsesCorrectTypeName()
    {
        var source = """
            class Program {
                public static void Main() {
                    with var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("var x = 42;", content);
        Assert.Contains("MistContext.Push<int>(x);", content);
        Assert.Contains("MistContext.Pop<int>();", content);
    }

    // ─── String type context ────────────────────────────────────────────

    [Fact]
    public void WithPushStatement_StringType_UsesCorrectTypeName()
    {
        var source = """
            class Program {
                public static void Main() {
                    with "hello";
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("MistContext.Push<string>(\"hello\");", content);
        Assert.Contains("MistContext.Pop<string>();", content);
    }
}