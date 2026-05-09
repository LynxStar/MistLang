// Feature: mist-regex-type, Property 4: Regex pattern matching type safety
// **Validates: Requirements 7.2, 7.3, 7.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for regex pattern matching type safety.
///
/// Property 4: Regex pattern matching type safety.
/// For any Mist program containing a regex literal pattern in an `is` expression or switch arm:
/// - If the scrutinee's static type is `string`, the type checker SHALL accept the pattern without diagnostics.
/// - If the scrutinee's static type is NOT `string`, the type checker SHALL emit a diagnostic error.
/// - The exhaustiveness checker SHALL treat any switch expression containing only regex patterns
///   (without a catch-all) as non-exhaustive.
///
/// **Validates: Requirements 7.2, 7.3, 7.5**
/// </summary>
public class RegexPatternMatchingTypeTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Compilation Helper ──────────────────────────────────────────────

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

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates a simple valid regex pattern for use in test programs.
    /// </summary>
    private static Gen<string> GenValidRegexPattern()
    {
        return Gen.Elements(
            "[a-z]+",
            "[A-Z0-9]+",
            "\\d+",
            "\\w+",
            ".*",
            "[abc]",
            "\\d{2,4}",
            "[^x]",
            "a?b+c*",
            "test",
            "hello",
            "[0-9]+",
            "\\s+",
            "(foo|bar)");
    }

    /// <summary>
    /// Represents a scrutinee type scenario for pattern matching tests.
    /// </summary>
#pragma warning disable CA1034
    public record ScrutineeScenario(string TypeName, string Declaration, bool IsString)
    {
        public override string ToString() => $"{TypeName} (isString={IsString})";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates a scrutinee scenario with a string type (should be accepted).
    /// </summary>
    private static Gen<ScrutineeScenario> GenStringScrutinee()
    {
        return Gen.Elements(
            new ScrutineeScenario("string", "string s = \"hello\";", true),
            new ScrutineeScenario("string", "string s = \"world\";", true),
            new ScrutineeScenario("string", "string s = \"test123\";", true),
            new ScrutineeScenario("string", "string s = \"\";", true),
            new ScrutineeScenario("string", "string s = \"foo bar\";", true));
    }

    /// <summary>
    /// Generates a scrutinee scenario with a non-string type (should emit diagnostic).
    /// </summary>
    private static Gen<ScrutineeScenario> GenNonStringScrutinee()
    {
        return Gen.Elements(
            new ScrutineeScenario("int", "int s = 42;", false),
            new ScrutineeScenario("int", "int s = 0;", false),
            new ScrutineeScenario("int", "int s = -1;", false),
            new ScrutineeScenario("bool", "bool s = true;", false),
            new ScrutineeScenario("bool", "bool s = false;", false),
            new ScrutineeScenario("double", "double s = 3.14;", false),
            new ScrutineeScenario("float", "float s = 1.0f;", false),
            new ScrutineeScenario("long", "long s = 100;", false),
            new ScrutineeScenario("char", "char s = 'a';", false),
            new ScrutineeScenario("byte", "byte s = 1;", false));
    }

    /// <summary>
    /// Generates a scenario for the `is` expression property test.
    /// </summary>
#pragma warning disable CA1034
    public record IsExpressionScenario(ScrutineeScenario Scrutinee, string Pattern)
    {
        public override string ToString() =>
            $"scrutinee={Scrutinee.TypeName}, pattern=`{Pattern}`, expectAccepted={Scrutinee.IsString}";
    }
#pragma warning restore CA1034

    private static Gen<IsExpressionScenario> GenIsExpressionScenario()
    {
        return Gen.OneOf(
            from scrutinee in GenStringScrutinee()
            from pattern in GenValidRegexPattern()
            select new IsExpressionScenario(scrutinee, pattern),
            from scrutinee in GenNonStringScrutinee()
            from pattern in GenValidRegexPattern()
            select new IsExpressionScenario(scrutinee, pattern));
    }

    /// <summary>
    /// Generates a scenario for the switch expression exhaustiveness test.
    /// </summary>
#pragma warning disable CA1034, CA1819
    public record SwitchExhaustivenessScenario(string[] Patterns, bool HasCatchAll)
    {
        public override string ToString() =>
            $"patterns=[{string.Join(", ", Patterns.Select(p => $"`{p}`"))}], catchAll={HasCatchAll}";
    }
#pragma warning restore CA1034, CA1819

    private static Gen<SwitchExhaustivenessScenario> GenSwitchExhaustivenessScenario()
    {
        return Gen.OneOf(
            // Without catch-all: should be non-exhaustive
            from count in Gen.Choose(1, 4)
            from patterns in Gen.ArrayOf(GenValidRegexPattern(), count)
            select new SwitchExhaustivenessScenario(patterns, false),
            // With catch-all: should be exhaustive
            from count in Gen.Choose(1, 3)
            from patterns in Gen.ArrayOf(GenValidRegexPattern(), count)
            select new SwitchExhaustivenessScenario(patterns, true));
    }

    // ─── Arbitrary Registration ─────────────────────────────────────────

    public static Arbitrary<IsExpressionScenario> ArbitraryIsExpressionScenario()
    {
        return GenIsExpressionScenario().ToArbitrary();
    }

    public static Arbitrary<SwitchExhaustivenessScenario> ArbitrarySwitchExhaustivenessScenario()
    {
        return GenSwitchExhaustivenessScenario().ToArbitrary();
    }

    // ─── Source Builders ────────────────────────────────────────────────

    private static string BuildIsExpressionSource(IsExpressionScenario scenario)
    {
        return $$"""
            class Program {
                public static void Main() {
                    {{scenario.Scrutinee.Declaration}}
                    bool result = s is `{{scenario.Pattern}}`;
                }
            }
            """;
    }

    private static string BuildSwitchExpressionSource(SwitchExhaustivenessScenario scenario)
    {
        var arms = string.Join("\n            ",
            scenario.Patterns.Select((p, i) => $"`{p}` => \"match{i}\","));

        if (scenario.HasCatchAll)
            arms += "\n            var _ => \"other\",";

        return $$"""
            class Program {
                public static void Main() {
                    string s = "hello";
                    string result = s switch {
                        {{arms}}
                    };
                }
            }
            """;
    }

    // ─── Property Tests ─────────────────────────────────────────────────

    /// <summary>
    /// Property 4a: Regex pattern matching type safety for `is` expressions.
    ///
    /// For any scrutinee type:
    /// - If the scrutinee is `string`, no MIST_TC_REGEX_SCRUTINEE diagnostic is emitted.
    /// - If the scrutinee is NOT `string`, MIST_TC_REGEX_SCRUTINEE diagnostic IS emitted.
    ///
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexPatternMatchingTypeTests) })]
    public Property RegexPattern_IsExpression_TypeSafety(IsExpressionScenario scenario)
    {
        var source = BuildIsExpressionSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);
        var scrutineeDiags = WithId(diagnostics, "MIST_TC_REGEX_SCRUTINEE");

        if (scenario.Scrutinee.IsString)
        {
            // String scrutinee: no MIST_TC_REGEX_SCRUTINEE diagnostic expected
            return (scrutineeDiags.Length == 0).ToProperty()
                .Label($"Expected no MIST_TC_REGEX_SCRUTINEE for string scrutinee, but got {scrutineeDiags.Length}.\nSource:\n{source}");
        }
        else
        {
            // Non-string scrutinee: MIST_TC_REGEX_SCRUTINEE diagnostic expected
            return (scrutineeDiags.Length > 0).ToProperty()
                .Label($"Expected MIST_TC_REGEX_SCRUTINEE for {scenario.Scrutinee.TypeName} scrutinee, but got none.\nSource:\n{source}");
        }
    }

    /// <summary>
    /// Property 4b: Switch expressions with only regex patterns are non-exhaustive.
    ///
    /// For any switch expression on a string scrutinee:
    /// - If only regex patterns are present (no catch-all), MIST_TC_0090 (non-exhaustive) is emitted.
    /// - If a catch-all is present, no non-exhaustive warning is emitted.
    ///
    /// **Validates: Requirement 7.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexPatternMatchingTypeTests) })]
    public Property RegexPattern_SwitchExpression_Exhaustiveness(SwitchExhaustivenessScenario scenario)
    {
        var source = BuildSwitchExpressionSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);
        var exhaustivenessDiags = WithId(diagnostics, "MIST_TC_0090");

        if (scenario.HasCatchAll)
        {
            // With catch-all: should be exhaustive (no warning)
            return (exhaustivenessDiags.Length == 0).ToProperty()
                .Label($"Expected no non-exhaustive warning with catch-all, but got {exhaustivenessDiags.Length}.\nSource:\n{source}");
        }
        else
        {
            // Without catch-all: should be non-exhaustive (warning emitted)
            return (exhaustivenessDiags.Length > 0).ToProperty()
                .Label($"Expected non-exhaustive warning without catch-all, but got none.\nSource:\n{source}");
        }
    }
}
