// Feature: mist-regex-type, Property 3: Conversion rule correctness
// **Validates: Requirements 6.1, 6.2, 6.3, 6.5, 6.6, 6.8, 6.9, 6.10**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for regex conversion rule correctness.
///
/// Property 3: Conversion rule correctness.
/// For any valid Mist program:
/// - Assigning a regex expression to a string-typed target SHALL be accepted without explicit cast.
/// - Assigning a non-literal string expression to a regex-typed target without explicit cast SHALL
///   produce a type mismatch diagnostic.
/// - Assigning a non-literal string expression to a regex-typed target WITH explicit cast SHALL be accepted.
/// - Assigning a string literal that is a valid .NET regex to a regex-typed target SHALL be accepted
///   without explicit cast.
/// - Assigning a string literal that is NOT a valid .NET regex to a regex-typed target SHALL produce
///   a diagnostic error.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.5, 6.6, 6.8, 6.9, 6.10**
/// </summary>
public class RegexConversionRulePropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Compilation Helper ──────────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source string in-process through the full pipeline
    /// (lexer → parser → semantic model → type checker) with stdlib loaded
    /// so that implicit/explicit operators on Standard.Regex are resolved.
    /// Returns all diagnostics from the build and type checker stages.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        // Load stdlib to get Standard.Regex with its implicit/explicit operators.
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var userUnit = new CompilationUnit(sourceText);
        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(parseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation, allAsts, ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/test", buildDiag);

        ReadonlyInferenceEngine.RunInference(model);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Scenario Types ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a conversion scenario to test.
    /// </summary>
#pragma warning disable CA1034
    public enum ConversionKind
    {
        /// <summary>regex → string (implicit): should always be accepted.</summary>
        RegexToString,

        /// <summary>string → regex without cast: should always be rejected (MIST_TC_0001).</summary>
        StringToRegexNoCast,

        /// <summary>string → regex with explicit cast: should always be accepted.</summary>
        StringToRegexWithCast,

        /// <summary>string literal → regex (valid pattern): should be accepted.</summary>
        StringLiteralToRegexValid,

        /// <summary>string literal → regex (invalid pattern): should be rejected (MIST_TC_REGEX_INVALID).</summary>
        StringLiteralToRegexInvalid,
    }

    public record ConversionScenario(ConversionKind Kind, string Pattern)
    {
        public override string ToString() => $"{Kind} pattern=\"{Pattern}\"";
    }
#pragma warning restore CA1034

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates strings that are known to be valid .NET regex patterns.
    /// These are safe to embed in string literals (no unescaped quotes).
    /// </summary>
    private static Gen<string> GenValidRegexPattern()
    {
        return Gen.Elements(
            "[a-z]+",
            "[A-Z0-9]+",
            "\\\\d+",
            "\\\\w+",
            ".*",
            "^hello$",
            "[abc]",
            "(foo|bar)",
            "\\\\d{2,4}",
            "[^x]",
            "a?b+c*",
            "test",
            "^[a-zA-Z0-9]+$",
            "[a-z]{1,10}",
            "\\\\s+",
            "(?:abc)+",
            "a|b|c",
            ".",
            "hello",
            "world");
    }

    /// <summary>
    /// Generates strings that are known to be invalid .NET regex patterns.
    /// These are safe to embed in string literals (no unescaped quotes).
    /// </summary>
    private static Gen<string> GenInvalidRegexPattern()
    {
        return Gen.Elements(
            "[",
            "(",
            "[invalid",
            "*",
            "+",
            "?",
            "[z-a]",
            "(abc",
            "(?<",
            "[\\\\");
    }

    /// <summary>
    /// Generates a valid regex pattern for use in backtick literals.
    /// These patterns don't need C# string escaping since they go in backticks.
    /// </summary>
    private static Gen<string> GenValidRegexForBacktick()
    {
        return Gen.Elements(
            "[a-z]+",
            "[A-Z0-9]+",
            "\\d+",
            "\\w+",
            ".*",
            "^hello$",
            "[abc]",
            "(foo|bar)",
            "\\d{2,4}",
            "[^x]",
            "a?b+c*",
            "test",
            "^[a-zA-Z0-9]+$",
            "[a-z]{1,10}",
            "\\s+",
            "(?:abc)+",
            "a|b|c",
            ".",
            "hello",
            "world");
    }

    /// <summary>
    /// Generates conversion scenarios covering all five conversion kinds.
    /// </summary>
    private static Gen<ConversionScenario> GenConversionScenario()
    {
        return Gen.Frequency(
            // regex → string (implicit): always accepted
            (2, GenValidRegexForBacktick().Select(p =>
                new ConversionScenario(ConversionKind.RegexToString, p))),
            // string → regex without cast: always rejected
            (2, GenValidRegexPattern().Select(p =>
                new ConversionScenario(ConversionKind.StringToRegexNoCast, p))),
            // string → regex with explicit cast: always accepted
            (2, GenValidRegexPattern().Select(p =>
                new ConversionScenario(ConversionKind.StringToRegexWithCast, p))),
            // string literal → regex (valid): accepted
            (2, GenValidRegexPattern().Select(p =>
                new ConversionScenario(ConversionKind.StringLiteralToRegexValid, p))),
            // string literal → regex (invalid): rejected
            (2, GenInvalidRegexPattern().Select(p =>
                new ConversionScenario(ConversionKind.StringLiteralToRegexInvalid, p))));
    }

    // ─── Arbitrary Registration ─────────────────────────────────────────

    public static Arbitrary<ConversionScenario> ArbitraryConversionScenario()
    {
        return GenConversionScenario().ToArbitrary();
    }

    // ─── Source Builders ────────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist program for the given conversion scenario.
    /// </summary>
    private static string BuildSource(ConversionScenario scenario)
    {
        return scenario.Kind switch
        {
            ConversionKind.RegexToString => $$"""
                class Program {
                    public static void Main() {
                        regex r = `{{scenario.Pattern}}`;
                        string s = r;
                    }
                }
                """,

            ConversionKind.StringToRegexNoCast => $$"""
                class Program {
                    public static void Main() {
                        string s = "{{scenario.Pattern}}";
                        regex r = s;
                    }
                }
                """,

            ConversionKind.StringToRegexWithCast => $$"""
                class Program {
                    public static void Main() {
                        string s = "{{scenario.Pattern}}";
                        regex r = (regex)s;
                    }
                }
                """,

            ConversionKind.StringLiteralToRegexValid => $$"""
                class Program {
                    public static void Main() {
                        regex r = "{{scenario.Pattern}}";
                    }
                }
                """,

            ConversionKind.StringLiteralToRegexInvalid => $$"""
                class Program {
                    public static void Main() {
                        regex r = "{{scenario.Pattern}}";
                    }
                }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    // ─── Property Test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: Conversion rule correctness.
    ///
    /// For each conversion scenario:
    /// 1. regex→string (implicit): should always be accepted (no type mismatch diagnostic)
    /// 2. string→regex without cast: should always be rejected (MIST_TC_0001 with "explicit cast")
    /// 3. string→regex with explicit cast: should always be accepted (no type mismatch or cast diagnostic)
    /// 4. string literal→regex (valid pattern): should be accepted (no type mismatch or regex invalid)
    /// 5. string literal→regex (invalid pattern): should be rejected (MIST_TC_REGEX_INVALID)
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.5, 6.6, 6.8, 6.9, 6.10**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexConversionRulePropertyTests) })]
    public Property ConversionRules_AreEnforcedCorrectly(ConversionScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);

        return scenario.Kind switch
        {
            ConversionKind.RegexToString => AssertRegexToStringAccepted(diagnostics, source),

            ConversionKind.StringToRegexNoCast => AssertStringToRegexRejected(diagnostics, source),

            ConversionKind.StringToRegexWithCast => AssertExplicitCastAccepted(diagnostics, source),

            ConversionKind.StringLiteralToRegexValid => AssertStringLiteralValidAccepted(diagnostics, source),

            ConversionKind.StringLiteralToRegexInvalid => AssertStringLiteralInvalidRejected(diagnostics, source, scenario.Pattern),

            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    // ─── Assertion Helpers ──────────────────────────────────────────────

    private static Property AssertRegexToStringAccepted(
        ImmutableArray<Diagnostic> diagnostics, string source)
    {
        // regex → string: no type mismatch diagnostic for user code
        // Filter to only user-code diagnostics (exclude stdlib-internal ones)
        var userTypeMismatch = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_0001" &&
            d.Message.Contains("'Regex'") && d.Message.Contains("'String'") &&
            !d.CompilationUnitPath.Contains("Standard/Bootstrap")).ToImmutableArray();

        return (userTypeMismatch.Length == 0).ToProperty()
            .Label($"Expected regex→string to be accepted (no type mismatch), " +
                   $"but got {userTypeMismatch.Length} diagnostic(s): " +
                   $"{string.Join("; ", userTypeMismatch.Select(d => d.Message))}\nSource:\n{source}");
    }

    private static Property AssertStringToRegexRejected(
        ImmutableArray<Diagnostic> diagnostics, string source)
    {
        // string → regex without cast: should emit MIST_TC_0001 with "explicit cast" message
        var typeMismatch = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_0001" &&
            d.Message.Contains("explicit cast")).ToImmutableArray();

        return (typeMismatch.Length > 0).ToProperty()
            .Label($"Expected string→regex without cast to be rejected (MIST_TC_0001 with 'explicit cast'), " +
                   $"but got 0 matching diagnostics.\nAll diagnostics: " +
                   $"{string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}\nSource:\n{source}");
    }

    private static Property AssertExplicitCastAccepted(
        ImmutableArray<Diagnostic> diagnostics, string source)
    {
        // string → regex with explicit cast: no type mismatch or invalid cast diagnostic in user code
        var typeMismatch = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_0001" &&
            d.Message.Contains("'String'") && d.Message.Contains("'Regex'") &&
            !d.CompilationUnitPath.Contains("Standard/Bootstrap")).ToImmutableArray();
        var noCast = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_0042" &&
            !d.CompilationUnitPath.Contains("Standard/Bootstrap")).ToImmutableArray();

        var hasIssue = typeMismatch.Length > 0 || noCast.Length > 0;
        return (!hasIssue).ToProperty()
            .Label($"Expected explicit cast (regex)s to be accepted, " +
                   $"but got {typeMismatch.Length} type mismatch and {noCast.Length} no-cast diagnostics.\n" +
                   $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}\nSource:\n{source}");
    }

    private static Property AssertStringLiteralValidAccepted(
        ImmutableArray<Diagnostic> diagnostics, string source)
    {
        // String literal → regex (valid): no type mismatch or regex invalid diagnostic in user code
        var typeMismatch = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_0001" &&
            d.Message.Contains("'String'") && d.Message.Contains("'Regex'") &&
            !d.CompilationUnitPath.Contains("Standard/Bootstrap")).ToImmutableArray();
        var regexInvalid = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_REGEX_INVALID" &&
            d.Message.Contains("Invalid regex pattern")).ToImmutableArray();

        var hasIssue = typeMismatch.Length > 0 || regexInvalid.Length > 0;
        return (!hasIssue).ToProperty()
            .Label($"Expected valid string literal→regex to be accepted, " +
                   $"but got {typeMismatch.Length} type mismatch and {regexInvalid.Length} regex invalid diagnostics.\n" +
                   $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}\nSource:\n{source}");
    }

    private static Property AssertStringLiteralInvalidRejected(
        ImmutableArray<Diagnostic> diagnostics, string source, string pattern)
    {
        // String literal → regex (invalid): should emit MIST_TC_REGEX_INVALID
        var regexInvalid = diagnostics.Where(d =>
            d.Id.Value == "MIST_TC_REGEX_INVALID" &&
            d.Message.Contains("Invalid regex pattern")).ToImmutableArray();

        // Double-check that the pattern is actually invalid
        bool isActuallyInvalid;
        try
        {
            _ = new Regex(pattern, RegexOptions.None);
            isActuallyInvalid = false;
        }
        catch (ArgumentException)
        {
            isActuallyInvalid = true;
        }

        if (!isActuallyInvalid)
        {
            // Pattern turned out to be valid — this shouldn't happen with our generator,
            // but if it does, the test should still pass (no diagnostic expected).
            return true.ToProperty()
                .Label("Pattern was actually valid — skipping invalid assertion.");
        }

        return (regexInvalid.Length > 0).ToProperty()
            .Label($"Expected MIST_TC_REGEX_INVALID for invalid pattern \"{pattern}\", " +
                   $"but got 0 matching diagnostics.\nAll diagnostics: " +
                   $"{string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}\nSource:\n{source}");
    }
}
