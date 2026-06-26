// Task 7.3: Unit tests for context block and method with-clause emission
// Validates: Requirements 12.7, 12.8

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that context blocks and method with-clauses emit correct C# code:
/// - with(expr) { ... } → MistContext.Push&lt;T&gt;(expr); try { ... } finally { MistContext.Pop&lt;T&gt;(); }
/// - Method with TypeReference identifier → var identifier = (TypeReference)MistContext.Peek&lt;TypeReference&gt;(); at method entry
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
/// </summary>
public sealed class ContextBlockAndWithClauseEmissionTests
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

    // ─── with(expr) { ... } block ───────────────────────────────────────

    [Fact]
    public void WithBlock_EmitsPushTryFinallyPop()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with(new Config()) {
                        var x = 42;
                    }
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("MistContext.Push<Config>(new Config());", content);
        Assert.Contains("try", content);
        Assert.Contains("var x = 42;", content);
        Assert.Contains("finally", content);
        Assert.Contains("MistContext.Pop<Config>();", content);
    }

    [Fact]
    public void WithBlock_PushBeforeTry_PopInFinally()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with(new Config()) {
                        var x = 1;
                    }
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        var pushIndex = content.IndexOf("MistContext.Push<Config>(new Config());");
        var tryIndex = content.IndexOf("try", pushIndex);
        var varXIndex = content.IndexOf("var x = 1;", tryIndex);
        var finallyIndex = content.IndexOf("finally", tryIndex);
        var popIndex = content.IndexOf("MistContext.Pop<Config>();", finallyIndex);

        Assert.True(pushIndex >= 0, "Push should be emitted");
        Assert.True(tryIndex > pushIndex, "try should come after Push");
        Assert.True(varXIndex > tryIndex, "var x = 1 should be inside try block");
        Assert.True(finallyIndex > varXIndex, "finally should come after the body");
        Assert.True(popIndex > finallyIndex, "Pop should be inside finally block");
    }

    [Fact]
    public void WithBlock_PrimitiveType_UsesCorrectTypeName()
    {
        var source = """
            class Program {
                public static void Main() {
                    with(42) {
                        var x = 1;
                    }
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("MistContext.Push<int>(42);", content);
        Assert.Contains("MistContext.Pop<int>();", content);
    }

    [Fact]
    public void WithBlock_DoesNotAffectStatementsAfterBlock()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    var before = 1;
                    with(new Config()) {
                        var inside = 2;
                    }
                    var after = 3;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        // "var after = 3;" should appear AFTER the finally block, not inside it
        var popIndex = content.IndexOf("MistContext.Pop<Config>();");
        var finallyCloseIndex = content.IndexOf("}", popIndex);
        var afterIndex = content.IndexOf("var after = 3;");

        Assert.True(afterIndex > finallyCloseIndex, "Statement after with-block should be outside the try/finally");
    }

    // ─── Method with-clause ─────────────────────────────────────────────

    [Fact]
    public void MethodWithClause_EmitsPeekAtMethodEntry()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                    DoWork();
                }
                public static void DoWork() with Config ctx {
                    var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("var ctx = (Config)MistContext.Peek<Config>();", content);
    }

    [Fact]
    public void MethodWithClause_PeekBeforeBodyStatements()
    {
        var source = """
            class Config { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                    DoWork();
                }
                public static void DoWork() with Config ctx {
                    var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        var peekIndex = content.IndexOf("var ctx = (Config)MistContext.Peek<Config>();");
        var varXIndex = content.IndexOf("var x = 42;", peekIndex);

        Assert.True(peekIndex >= 0, "Peek should be emitted");
        Assert.True(varXIndex > peekIndex, "var x = 42 should come after the peek binding");
    }

    [Fact]
    public void MethodWithClause_MultipleWithClauses_EmitsMultiplePeeks()
    {
        var source = """
            class Config { }
            class Logger { }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                    with var log = new Logger();
                    DoWork();
                }
                public static void DoWork() with Config ctx with Logger log {
                    var x = 42;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("var ctx = (Config)MistContext.Peek<Config>();", content);
        Assert.Contains("var log = (Logger)MistContext.Peek<Logger>();", content);

        // Both peeks should come before the body statement
        var peekCtxIndex = content.IndexOf("var ctx = (Config)MistContext.Peek<Config>();");
        var peekLogIndex = content.IndexOf("var log = (Logger)MistContext.Peek<Logger>();");
        var varXIndex = content.IndexOf("var x = 42;");

        Assert.True(peekCtxIndex < varXIndex, "Config peek should come before body");
        Assert.True(peekLogIndex < varXIndex, "Logger peek should come before body");
    }

    [Fact]
    public void ConstructorWithClause_EmitsPeekAtConstructorEntry()
    {
        var source = """
            class Config { }
            class Service {
                public Service() with Config ctx {
                    var x = 42;
                }
            }
            class Program {
                public static void Main() {
                    with var cfg = new Config();
                    var svc = new Service();
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("var ctx = (Config)MistContext.Peek<Config>();", content);
    }

    [Fact]
    public void MethodWithClause_PrimitiveType_UsesCorrectTypeName()
    {
        var source = """
            class Program {
                public static void Main() {
                    with 42;
                    DoWork();
                }
                public static void DoWork() with int val {
                    var x = 1;
                }
            }
            """;

        var files = CompileToFiles(source);
        var content = GetMainFileContent(files);

        Assert.Contains("var val = (int)MistContext.Peek<int>();", content);
    }
}