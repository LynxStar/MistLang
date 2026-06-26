// **Validates: Requirement 7.5**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that regex patterns in switch expressions are treated
/// as non-exhaustive by the exhaustiveness checker.
///
/// Regex patterns over strings can never prove they cover all possible string
/// values, so a switch expression with only regex patterns (and no catch-all)
/// must produce a non-exhaustive warning.
///
/// **Validates: Requirement 7.5**
/// </summary>
public class RegexExhaustivenessTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── AST construction helpers ────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static PatternNode MakeRegexConstantPattern(string pattern)
    {
        PrimaryPatternNode primary = new ConstantPatternPrimaryPatternNode
        {
            Literal = new RegexConstantConstantLiteralNode
            {
                Value = Tok($"`{pattern}`"),
            },
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeCatchAllPattern()
    {
        PrimaryPatternNode primary = new VarPatternPrimaryPatternNode
        {
            Name = Tok("_"),
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeStringConstantPattern(string value)
    {
        PrimaryPatternNode primary = new ConstantPatternPrimaryPatternNode
        {
            Literal = new StringConstantConstantLiteralNode
            {
                Value = Tok($"\"{value}\""),
            },
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    // ─── Direct exhaustiveness checker tests ─────────────────────────────

    [Fact]
    public void RegexPatternOnly_StringScrutinee_IsNotExhaustive()
    {
        var arms = new List<(PatternNode Pattern, bool HasGuard)>
        {
            (MakeRegexConstantPattern("[a-z]+"), false),
            (MakeRegexConstantPattern("[0-9]+"), false),
        };

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(
            LegacyPrimitiveTypes.String, arms);

        Assert.NotEmpty(uncovered);
        Assert.Contains("_", uncovered);
    }

    [Fact]
    public void RegexPatternWithCatchAll_StringScrutinee_IsExhaustive()
    {
        var arms = new List<(PatternNode Pattern, bool HasGuard)>
        {
            (MakeRegexConstantPattern("[a-z]+"), false),
            (MakeCatchAllPattern(), false),
        };

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(
            LegacyPrimitiveTypes.String, arms);

        Assert.Empty(uncovered);
    }

    [Fact]
    public void SingleRegexPattern_NoCatchAll_IsNotExhaustive()
    {
        var arms = new List<(PatternNode Pattern, bool HasGuard)>
        {
            (MakeRegexConstantPattern(".*"), false),
        };

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(
            LegacyPrimitiveTypes.String, arms);

        Assert.NotEmpty(uncovered);
    }

    [Fact]
    public void MixedRegexAndStringConstants_NoCatchAll_IsNotExhaustive()
    {
        var arms = new List<(PatternNode Pattern, bool HasGuard)>
        {
            (MakeRegexConstantPattern("[a-z]+"), false),
            (MakeStringConstantPattern("hello"), false),
        };

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(
            LegacyPrimitiveTypes.String, arms);

        Assert.NotEmpty(uncovered);
    }

    [Fact]
    public void GuardedCatchAll_WithRegexPatterns_IsNotExhaustive()
    {
        var arms = new List<(PatternNode Pattern, bool HasGuard)>
        {
            (MakeRegexConstantPattern("[a-z]+"), false),
            (MakeCatchAllPattern(), true), // guarded catch-all doesn't count
        };

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(
            LegacyPrimitiveTypes.String, arms);

        Assert.NotEmpty(uncovered);
    }

    // ─── Integration test via full compilation ───────────────────────────

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
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        ReadonlyInferenceEngine.RunInference(model);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    [Fact]
    public void SwitchExpression_OnlyRegexPatterns_EmitsNonExhaustiveWarning()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello";
                    string result = s switch {
                        `[a-z]+` => "letters",
                        `[0-9]+` => "digits",
                    };
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var exhaustivenessDiags = WithId(diagnostics, "MIST_TC_0090");

        _ = Assert.Single(exhaustivenessDiags);
        Assert.Contains("not exhaustive", exhaustivenessDiags[0].Message);
    }

    [Fact]
    public void SwitchExpression_RegexPatternsWithCatchAll_NoWarning()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello";
                    string result = s switch {
                        `[a-z]+` => "letters",
                        `[0-9]+` => "digits",
                        var _ => "other",
                    };
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var exhaustivenessDiags = WithId(diagnostics, "MIST_TC_0090");

        Assert.Empty(exhaustivenessDiags);
    }
}