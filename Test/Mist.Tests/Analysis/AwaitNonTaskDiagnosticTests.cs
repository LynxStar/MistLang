// **Validates: Requirement 4.3**

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
/// Unit tests verifying that MIST_TC_0105 is emitted when <c>await</c> is
/// applied to an expression whose type is not <c>Task&lt;T&gt;</c>.
/// </summary>
public class AwaitNonTaskDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

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
        var model = Builder.Build(
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

    /// <summary>
    /// Compiles source with stdlib loaded (for tests that need Task&lt;T&gt; resolution).
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

    [Fact]
    public void AwaitOnIntExpression_EmitsMIST_TC_0105()
    {
        const string source = """
            class Foo {
                public void M() {
                    var x = await 42;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0105");
        Assert.Contains("await", diag.Message);
        Assert.Contains("Task<T>", diag.Message);
    }

    [Fact]
    public void AwaitOnIntVariable_EmitsMIST_TC_0105_WithActualType()
    {
        const string source = """
            class Foo {
                public void M() {
                    var n = 10;
                    var x = await n;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0105");
        Assert.Contains("int", diag.Message);
    }

    [Fact]
    public void AwaitOnTaskExpression_DoesNotEmitMIST_TC_0105()
    {
        const string source = """
            using Standard;
            class Foo {
                public async int GetValue() { return 1; }
                public void M() {
                    var t = async GetValue();
                    var x = await t;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0105");
    }

    [Fact]
    public void AwaitOnBoolExpression_MessageIncludesActualType()
    {
        const string source = """
            class Foo {
                public void M() {
                    var b = true;
                    var x = await b;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0105");
        Assert.Contains("bool", diag.Message);
        Assert.Contains("Task<T>", diag.Message);
    }

    [Fact]
    public void AwaitOnUnresolvedVariable_DoesNotEmitMIST_TC_0105()
    {
        // When the operand type is UnknownType (prior error), suppress the diagnostic.
        const string source = """
            class Foo {
                public void M() {
                    var x = await undeclaredVariable;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0105");
    }

    [Fact]
    public void AwaitOnDirectAsyncMethodCall_DoesNotEmitMIST_TC_0105()
    {
        // In a sync context, async method calls are auto-unwrapped to T,
        // but await on the result of async functor should work.
        const string source = """
            using Standard;
            class Foo {
                public async int Compute() { return 42; }
                public void M() {
                    var result = await async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0105");
    }
}
