// **Validates: Requirement 3.7**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_TC_0107 is emitted when the <c>async</c>
/// functor is applied to an expression that is not a method invocation.
/// </summary>
public class AsyncFunctorOperandDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

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
    public void AsyncOnVariableReference_EmitsMIST_TC_0107()
    {
        const string source = """
            using Standard;
            class Foo {
                public void M() {
                    var x = 42;
                    var t = async x;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0107");
        Assert.Contains("async", diag.Message);
        Assert.Contains("method invocation", diag.Message);
        Assert.Contains("variable reference", diag.Message);
    }

    [Fact]
    public void AsyncOnLiteral_EmitsMIST_TC_0107()
    {
        const string source = """
            using Standard;
            class Foo {
                public void M() {
                    var t = async 42;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0107");
        Assert.Contains("literal", diag.Message);
    }

    [Fact]
    public void AsyncOnPropertyAccess_EmitsMIST_TC_0107()
    {
        const string source = """
            using Standard;
            class Foo {
                public int Value { get { return 1; } }
                public void M() {
                    var t = async this.Value;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0107");
        Assert.Contains("property access", diag.Message);
    }

    [Fact]
    public void AsyncOnMethodInvocation_DoesNotEmitMIST_TC_0107()
    {
        const string source = """
            using Standard;
            class Foo {
                public int Compute() { return 1; }
                public void M() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0107");
    }

    [Fact]
    public void AsyncOnDotMethodCall_DoesNotEmitMIST_TC_0107()
    {
        const string source = """
            using Standard;
            class Foo {
                public int Compute() { return 1; }
                public void M() {
                    var t = async this.Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0107");
    }

    [Fact]
    public void AsyncOnUnresolvedIdentifier_DoesNotEmitMIST_TC_0107()
    {
        // When the operand type is UnknownType (prior error), suppress the diagnostic.
        const string source = """
            using Standard;
            class Foo {
                public void M() {
                    var t = async undeclaredVariable;
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0107");
    }
}