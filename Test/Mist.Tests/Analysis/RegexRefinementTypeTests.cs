// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Tests for regex refinement type support (Requirements 11.1–11.6).
/// Verifies:
/// - Regex refinement predicates are only valid on string base types (MIST_TC_REGEX_REFINEMENT)
/// - Regex pattern syntax is validated in refinement predicates (MIST_TC_REGEX_INVALID)
/// - String literals are evaluated against regex at compile time (MIST_TC_0097)
/// - Non-constant strings emit MIST_TC_0098
/// </summary>
public class RegexRefinementTypeTests
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

    private static (ISemanticModel? Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return (null, parseDiag.ToImmutableArray());
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        return (model, buildDiag.ToImmutableArray());
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.1: Regex refinement on string base type creates RefinementType
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexRefinement_OnString_CreatesRefinementType()
    {
        const string source = "type Email = string `^[^@]+@[^@]+$`;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);
        Assert.Empty(WithId(diagnostics, "MIST_TC_REGEX_REFINEMENT"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_REGEX_INVALID"));

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Email" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var declaredType = aliasSymbol.DeclaredType;
        Assert.NotNull(declaredType);
        _ = Assert.IsType<RefinementType>(declaredType);

        var refinementType = (RefinementType)declaredType;
        Assert.Equal("Email", refinementType.Name);
        Assert.Equal("string", refinementType.BaseType.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.3: Regex refinement on non-string emits MIST_TC_REGEX_REFINEMENT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexRefinement_OnInt_EmitsDiagnostic()
    {
        const string source = "type Bad = int `^[0-9]+$`;";

        var (_, diagnostics) = BuildModel(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_REGEX_REFINEMENT"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("string", diag.Message);
        Assert.Contains("int", diag.Message);
    }

    [Fact]
    public void RegexRefinement_OnBool_EmitsDiagnostic()
    {
        const string source = "type Bad = bool `^true$`;";

        var (_, diagnostics) = BuildModel(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_REGEX_REFINEMENT"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("bool", diag.Message);
    }

    [Fact]
    public void RegexRefinement_OnByte_EmitsDiagnostic()
    {
        const string source = "type Bad = byte `^[0-9]$`;";

        var (_, diagnostics) = BuildModel(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_REGEX_REFINEMENT"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("byte", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.4: Invalid regex pattern in refinement emits MIST_TC_REGEX_INVALID
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexRefinement_InvalidPattern_EmitsDiagnostic()
    {
        const string source = "type Bad = string `^[invalid`;";

        var (_, diagnostics) = BuildModel(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_REGEX_INVALID"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("Invalid regex pattern", diag.Message);
    }

    [Fact]
    public void RegexRefinement_ValidPattern_NoDiagnostic()
    {
        const string source = "type Email = string `^[^@]+@[^@]+$`;";

        var (_, diagnostics) = BuildModel(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_REGEX_INVALID"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.5: String literal evaluated against regex at compile time
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexRefinement_MatchingLiteral_Accepted()
    {
        const string source = """
            type Email = string `^[^@]+@[^@]+$`;
            class Program {
                public static void Main() {
                    Email e = "test@example.com";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void RegexRefinement_NonMatchingLiteral_EmitsDiagnostic()
    {
        const string source = """
            type Email = string `^[^@]+@[^@]+$`;
            class Program {
                public static void Main() {
                    Email e = "not-an-email";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("not-an-email", diag.Message);
    }

    [Fact]
    public void RegexRefinement_FullMatchSemantics_PartialMatchRejected()
    {
        // The regex `[a-z]+` should require the ENTIRE string to match.
        // "abc123" partially matches [a-z]+ but not fully.
        const string source = """
            type LowerOnly = string `[a-z]+`;
            class Program {
                public static void Main() {
                    LowerOnly x = "abc123";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Contains("abc123", diag.Message);
    }

    [Fact]
    public void RegexRefinement_FullMatchSemantics_FullMatchAccepted()
    {
        const string source = """
            type LowerOnly = string `[a-z]+`;
            class Program {
                public static void Main() {
                    LowerOnly x = "abcdef";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.6: Non-constant string emits MIST_TC_0098
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexRefinement_NonConstantString_EmitsDiagnostic()
    {
        const string source = """
            type Email = string `^[^@]+@[^@]+$`;
            class Program {
                public static void Main() {
                    string input = "test@example.com";
                    Email e = input;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("pattern matching", diag.Message);
    }

    [Fact]
    public void RegexRefinement_NonConstantString_MessageContainsTypeName()
    {
        const string source = """
            type Email = string `^[^@]+@[^@]+$`;
            class Program {
                public static void Main() {
                    string input = "test@example.com";
                    Email e = input;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));
        Assert.Contains("Email", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegexRefinement_EmptyPattern_MatchesEmptyString()
    {
        const string source = """
            type Empty = string ``;
            class Program {
                public static void Main() {
                    Empty e = "";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
    }

    [Fact]
    public void RegexRefinement_EmptyPattern_RejectsNonEmptyString()
    {
        const string source = """
            type Empty = string ``;
            class Program {
                public static void Main() {
                    Empty e = "hello";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        _ = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));
    }

    [Fact]
    public void RegexRefinement_PatternWithEscapedBacktick_Works()
    {
        // Pattern: ^a\`b$ (matches the literal string a`b)
        const string source = """
            type HasBacktick = string `^a\`b$`;
            class Program {
                public static void Main() {
                    HasBacktick x = "a`b";
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_REGEX_INVALID"));
    }
}