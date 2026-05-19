// **Validates: Requirement 2.6**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_TC_0106 is emitted when an async method
/// explicitly declares <c>Task&lt;T&gt;</c> as its return type.
/// </summary>
public class AsyncExplicitTaskReturnTypeDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles source with stdlib loaded (needed for Task&lt;T&gt; resolution).
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileWithStdlibAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    /// <summary>
    /// Compiles source without stdlib (for tests that don't need Task&lt;T&gt;).
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        var buildDiag = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    [Fact]
    public void AsyncMethodWithExplicitTaskReturnType_EmitsMIST_TC_0106()
    {
        const string source = """
            using Standard;
            class Foo {
                public async Task<int> Compute() { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0106");
        Assert.Contains("Compute", diag.Message);
        Assert.Contains("Task<T>", diag.Message);
        Assert.Contains("compiler wraps the return type automatically", diag.Message);
    }

    [Fact]
    public void AsyncMethodWithNonTaskReturnType_DoesNotEmitMIST_TC_0106()
    {
        const string source = """
            class Foo {
                public async int Compute() { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0106");
    }

    [Fact]
    public void NonAsyncMethodWithTaskReturnType_DoesNotEmitMIST_TC_0106()
    {
        const string source = """
            using Standard;
            class Foo {
                public Task<int> GetTask() { return default; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0106");
    }

    [Fact]
    public void AsyncMethodWithExplicitTaskVoid_EmitsMIST_TC_0106()
    {
        const string source = """
            using Standard;
            class Foo {
                public async Task<void> DoWork() { return; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0106");
        Assert.Contains("DoWork", diag.Message);
    }

    [Fact]
    public void AsyncMethodWithExplicitTaskReturnType_MessageFormat()
    {
        const string source = """
            using Standard;
            class Bar {
                public async Task<string> GetName() { return "hello"; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0106");
        Assert.Equal(
            "Async method 'GetName' must not explicitly declare 'Task<T>' as its return type; the compiler wraps the return type automatically",
            diag.Message);
    }
}
