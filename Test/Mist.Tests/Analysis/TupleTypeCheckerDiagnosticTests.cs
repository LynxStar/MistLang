// Feature: value-tuples — Unit tests for type checker diagnostics
// **Validates: Requirements 4.6**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for tuple-specific type checker diagnostics:
/// arity mismatch and non-tuple RHS in deconstruction.
/// </summary>
public class TupleTypeCheckerDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) Compile(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        var ast = parseResult.Root ?? throw new InvalidOperationException("Parse produced no root node.");

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        return (model, tcDiag.ToImmutableArray());
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Any(d => d.Id.Value == id);
    }

    // ─── Requirement 4.6: Arity mismatch ────────────────────────────────

    [Fact]
    public void TupleDeconstructionArityMismatch_TwoTargetsThreeElements_EmitsDiagnostic()
    {
        const string source = """
            class Test {
              void Run() {
                var (a, b) = (1, 2, 3);
              }
            }
            """;

        var (_, diagnostics) = Compile(source);
        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0061"),
            $"Expected MIST_TC_0061 but got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    [Fact]
    public void TupleDeconstructionArityMismatch_ThreeTargetsTwoElements_EmitsDiagnostic()
    {
        const string source = """
            class Test {
              void Run() {
                var (a, b, c) = (1, 2);
              }
            }
            """;

        var (_, diagnostics) = Compile(source);
        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0061"),
            $"Expected MIST_TC_0061 but got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    [Fact]
    public void TupleDeconstructionArityMatch_TwoTargetsTwoElements_NoDiagnostic()
    {
        const string source = """
            class Test {
              void Run() {
                var (a, b) = (1, 2);
              }
            }
            """;

        var (_, diagnostics) = Compile(source);
        Assert.False(HasDiagnostic(diagnostics, "MIST_TC_0061"),
            "Expected no MIST_TC_0061 for matching arity");
    }

    // ─── Requirement 4.6: Non-tuple RHS ─────────────────────────────────

    [Fact]
    public void TupleDeconstructionNonTupleRhs_IntRhs_EmitsDiagnostic()
    {
        const string source = """
            class Test {
              void Run() {
                int x = 42;
                var (a, b) = x;
              }
            }
            """;

        var (_, diagnostics) = Compile(source);
        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0064"),
            $"Expected MIST_TC_0064 but got: {string.Join(", ", diagnostics.Select(d => d.Id.Value))}");
    }

    [Fact]
    public void TupleDeconstructionNonTupleRhs_TupleRhs_NoDiagnostic()
    {
        const string source = """
            class Test {
              void Run() {
                var (a, b) = (1, 2);
              }
            }
            """;

        var (_, diagnostics) = Compile(source);
        Assert.False(HasDiagnostic(diagnostics, "MIST_TC_0064"),
            "Expected no MIST_TC_0064 for tuple RHS");
    }
}