// **Validates: Requirement 2.5**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_TC_0108 is emitted when the <c>async</c>
/// modifier is applied to a constructor or operator declaration.
/// </summary>
public class AsyncConstructorOperatorDiagnosticTests
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
    public void AsyncOnConstructor_EmitsMIST_TC_0108()
    {
        const string source = """
            class Foo {
                public async Foo() {}
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0108");
        Assert.Equal("The 'async' modifier is not valid on constructors", diag.Message);
    }

    [Fact]
    public void AsyncOnOperator_EmitsMIST_TC_0108()
    {
        const string source = """
            class Foo {
                public static async int operator +(Foo a, Foo b) { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0108");
        Assert.Equal("The 'async' modifier is not valid on operators", diag.Message);
    }

    [Fact]
    public void AsyncOnCastOperator_EmitsMIST_TC_0108()
    {
        const string source = """
            class Foo {
                public static async implicit operator int(Foo value) { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0108");
        Assert.Equal("The 'async' modifier is not valid on operators", diag.Message);
    }

    [Fact]
    public void ConstructorWithoutAsync_DoesNotEmitMIST_TC_0108()
    {
        const string source = """
            class Foo {
                public Foo() {}
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0108");
    }

    [Fact]
    public void OperatorWithoutAsync_DoesNotEmitMIST_TC_0108()
    {
        const string source = """
            class Foo {
                public static int operator +(Foo a, Foo b) { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0108");
    }

    [Fact]
    public void AsyncOnMethod_DoesNotEmitMIST_TC_0108()
    {
        const string source = """
            class Foo {
                public async int Compute() { return 1; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0108");
    }
}