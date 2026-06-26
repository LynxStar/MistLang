// **Validates: Requirement 2.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_TC_0104 is emitted when a method has both
/// the <c>async</c> and <c>intrinsic</c> modifiers.
/// </summary>
public class AsyncIntrinsicDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
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
    public void AsyncAndIntrinsic_EmitsMIST_TC_0104()
    {
        const string source = """
            class Foo {
                intrinsic public async int Compute();
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0104");
        Assert.Contains("Compute", diag.Message);
        Assert.Contains("async", diag.Message);
        Assert.Contains("intrinsic", diag.Message);
    }

    [Fact]
    public void AsyncWithoutIntrinsic_DoesNotEmitMIST_TC_0104()
    {
        const string source = """
            class Foo {
                public async int Compute() { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0104");
    }

    [Fact]
    public void IntrinsicWithoutAsync_DoesNotEmitMIST_TC_0104()
    {
        const string source = """
            intrinsic class Foo {
                intrinsic public int Compute();
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0104");
    }

    [Fact]
    public void AsyncAndIntrinsic_MessageContainsMethodName()
    {
        const string source = """
            class Bar {
                public async intrinsic void DoWork();
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0104");
        Assert.Equal("Method 'DoWork' cannot be both 'async' and 'intrinsic'", diag.Message);
    }
}