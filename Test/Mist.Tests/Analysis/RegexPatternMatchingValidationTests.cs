// **Validates: Requirements 7.2, 7.3, 7.4, 7.7, 7.8**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for regex pattern matching validation in the type checker.
/// Verifies that:
/// - Regex patterns in `is` expressions require a string scrutinee (Req 7.2)
/// - Non-string scrutinees emit MIST_TC_REGEX_SCRUTINEE diagnostic (Req 7.3)
/// - Invalid regex patterns at pattern position emit MIST_TC_REGEX_INVALID (Req 7.7)
/// - Capture groups in regex patterns are ignored — no bindings introduced (Req 7.8)
///
/// **Validates: Requirements 7.2, 7.3, 7.4, 7.7, 7.8**
/// </summary>
public class RegexPatternMatchingValidationTests
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

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.2: Regex pattern in `is` expression with string scrutinee
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexPattern_IsExpression_StringScrutinee_NoDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello";
                    bool result = s is `[a-z]+`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");

        Assert.Empty(regexScrutineeDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.3: Non-string scrutinee emits MIST_TC_REGEX_SCRUTINEE
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexPattern_IsExpression_IntScrutinee_EmitsDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int x = 42;
                    bool result = x is `[0-9]+`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");

        var diag = Assert.Single(regexScrutineeDiags);
        Assert.Contains("Regex patterns require a 'string' scrutinee", diag.Message);
    }

    [Fact]
    public void RegexPattern_IsExpression_BoolScrutinee_EmitsDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    bool b = true;
                    bool result = b is `true|false`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");

        var diag = Assert.Single(regexScrutineeDiags);
        Assert.Contains("Regex patterns require a 'string' scrutinee", diag.Message);
    }

    [Fact]
    public void RegexPattern_IsExpression_NonStringScrutinee_MessageContainsActualType()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int x = 42;
                    bool result = x is `[0-9]+`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE"));

        Assert.Contains("int", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.3: Regex pattern in switch arm with non-string scrutinee
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexPattern_SwitchArm_IntScrutinee_EmitsDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int x = 42;
                    string result = x switch {
                        `[0-9]+` => "digits",
                        _ => "other",
                    };
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");

        var diag = Assert.Single(regexScrutineeDiags);
        Assert.Contains("Regex patterns require a 'string' scrutinee", diag.Message);
    }

    [Fact]
    public void RegexPattern_SwitchArm_StringScrutinee_NoDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello";
                    string result = s switch {
                        `[a-z]+` => "letters",
                        _ => "other",
                    };
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");

        Assert.Empty(regexScrutineeDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.7: Invalid regex pattern at pattern position
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexPattern_InvalidSyntax_EmitsDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello";
                    bool result = s is `[invalid`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexInvalidDiags = WithId(diagnostics, "MIST_TC_REGEX_INVALID");

        var diag = Assert.Single(regexInvalidDiags);
        Assert.Contains("Invalid regex pattern", diag.Message);
    }

    [Fact]
    public void RegexPattern_ValidSyntax_NoInvalidDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello";
                    bool result = s is `^[a-zA-Z0-9]+$`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexInvalidDiags = WithId(diagnostics, "MIST_TC_REGEX_INVALID");

        Assert.Empty(regexInvalidDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.8: Capture groups are ignored (no bindings introduced)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexPattern_WithCaptureGroups_NoDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "hello world";
                    bool result = s is `(\w+)\s(\w+)`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");
        var regexInvalidDiags = WithId(diagnostics, "MIST_TC_REGEX_INVALID");

        Assert.Empty(regexScrutineeDiags);
        Assert.Empty(regexInvalidDiags);
    }

    [Fact]
    public void RegexPattern_WithNamedCaptureGroups_NoDiagnostic()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string s = "2024-01-15";
                    bool result = s is `(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})`;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var regexScrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");
        var regexInvalidDiags = WithId(diagnostics, "MIST_TC_REGEX_INVALID");

        Assert.Empty(regexScrutineeDiags);
        Assert.Empty(regexInvalidDiags);
    }
}