// **Validates: Requirements 5.3**
//
// Unit tests verifying that using a generic type alias with the wrong number
// of type arguments produces the MIST_SB_0063 diagnostic.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for type alias argument count mismatch diagnostic (MIST_SB_0063).
///
/// Validates: Requirements 5.3
///
/// When a generic type alias is used with an incorrect number of type arguments,
/// the compiler SHALL report a type argument count mismatch diagnostic error.
/// </summary>
public class TypeAliasArgumentMismatchTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles Mist source and returns all diagnostics from both the semantic model
    /// builder and the type checker analyzer.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
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

        // Combine semantic model builder diagnostics and type checker diagnostics.
        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_SB_0063: Type alias argument count mismatch
    // Requirement 5.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericAlias_TooFewTypeArguments_EmitsMIST_SB_0063()
    {
        // Pair<T, U> expects 2 type arguments, but only 1 is provided.
        const string source = """
            type Pair<T, U> = int;
            class Program {
                public static void Main() {
                    Pair<int> x = 0;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_SB_0063");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("Pair", diag.Message);
        Assert.Contains("2", diag.Message);
        Assert.Contains("1", diag.Message);
    }

    [Fact]
    public void GenericAlias_TooManyTypeArguments_EmitsMIST_SB_0063()
    {
        // Wrapper<T> expects 1 type argument, but 3 are provided.
        const string source = """
            type Wrapper<T> = int;
            class Program {
                public static void Main() {
                    Wrapper<int, string, bool> x = 0;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_SB_0063");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("Wrapper", diag.Message);
        Assert.Contains("1", diag.Message);
        Assert.Contains("3", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Positive test: correct number of type arguments (no false positives)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericAlias_CorrectTypeArguments_NoDiagnostic()
    {
        // Pair<T, U> expects 2 type arguments, and 2 are provided — no error.
        const string source = """
            type Pair<T, U> = int;
            class Program {
                public static void Main() {
                    Pair<int, string> x = 0;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_SB_0063");
    }
}
