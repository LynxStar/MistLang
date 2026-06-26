// **Validates: Requirement 3.6**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_TC_0109 (warning) is emitted when the
/// <c>async</c> functor is applied to an async method in an async context,
/// producing <c>Task&lt;Task&lt;T&gt;&gt;</c>.
/// </summary>
public class AsyncFunctorDoubleWrapDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static ImmutableArray<Diagnostic> CompileWithStdlibAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    [Fact]
    public void AsyncFunctorOnAsyncMethodInAsyncContext_EmitsWarning()
    {
        const string source = """
            using Standard;
            class Foo {
                public async int Compute() { return 1; }
                public async void Caller() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0109");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("Task<Task<T>>", diag.Message);
        Assert.Contains("likely unintended", diag.Message);
    }

    [Fact]
    public void AsyncFunctorOnAsyncMethodInSyncContext_DoesNotEmitWarning()
    {
        const string source = """
            using Standard;
            class Foo {
                public async int Compute() { return 1; }
                public void Caller() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0109");
    }

    [Fact]
    public void AsyncFunctorOnSyncMethodInAsyncContext_DoesNotEmitWarning()
    {
        const string source = """
            using Standard;
            class Foo {
                public int Compute() { return 1; }
                public async void Caller() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0109");
    }

    [Fact]
    public void AsyncFunctorOnSyncMethodInSyncContext_DoesNotEmitWarning()
    {
        const string source = """
            using Standard;
            class Foo {
                public int Compute() { return 1; }
                public void Caller() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0109");
    }

    [Fact]
    public void AsyncFunctorOnAsyncDotMethodInAsyncContext_EmitsWarning()
    {
        const string source = """
            using Standard;
            class Foo {
                public async void DoWork() { }
                public async void Caller() {
                    var t = async this.DoWork();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0109");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }

    [Fact]
    public void AsyncFunctorOnAsyncVoidMethodInAsyncContext_EmitsWarning()
    {
        const string source = """
            using Standard;
            class Foo {
                public async void DoWork() { }
                public async void Caller() {
                    var t = async DoWork();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0109");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }
}