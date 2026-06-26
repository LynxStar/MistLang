// Feature: mist-regex-type, Property 2: Regex literal compile-time validation
// **Validates: Requirements 4.6, 5.3, 5.4, 7.7**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for compile-time regex validation.
///
/// Property 2: Regex literal compile-time validation.
/// For any string P, if P is a syntactically valid .NET regular expression
/// (accepted by new System.Text.RegularExpressions.Regex(P, RegexOptions.None)
/// without throwing), then a Mist program containing the regex literal `P`
/// SHALL compile without diagnostics related to regex validation. Conversely,
/// if P is NOT a valid .NET regex, the compiler SHALL emit a diagnostic error
/// indicating the regex syntax error.
///
/// **Validates: Requirements 4.6, 5.3, 5.4, 7.7**
/// </summary>
public class RegexCompileTimeValidationTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Compilation Helper ──────────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source string in-process through the full pipeline
    /// (lexer → parser → semantic model → type checker) and returns all
    /// diagnostics from the semantic model builder and type checker stages.
    /// </summary>
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

    // ─── Pattern Generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates strings that are known to be valid .NET regex patterns.
    /// </summary>
    private static Gen<string> GenValidRegexPattern()
    {
        return Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant("[a-z]+"),
            Gen.Constant("[A-Z0-9]+"),
            Gen.Constant("\\d+"),
            Gen.Constant("\\w+"),
            Gen.Constant(".*"),
            Gen.Constant("^hello$"),
            Gen.Constant("[abc]"),
            Gen.Constant("(foo|bar)"),
            Gen.Constant("\\d{2,4}"),
            Gen.Constant("[^x]"),
            Gen.Constant("a?b+c*"),
            Gen.Constant("test"),
            Gen.Constant("^[a-zA-Z0-9]+$"),
            Gen.Constant("(\\d{3})-(\\d{4})"),
            Gen.Constant("[a-z]{1,10}"),
            Gen.Constant("\\s+"),
            Gen.Constant("(?:abc)+"),
            Gen.Constant("a|b|c"),
            Gen.Constant("."));
    }

    /// <summary>
    /// Generates strings that are known to be invalid .NET regex patterns.
    /// </summary>
    private static Gen<string> GenInvalidRegexPattern()
    {
        return Gen.OneOf(
            Gen.Constant("["),
            Gen.Constant("("),
            Gen.Constant("[invalid"),
            Gen.Constant("(?P<"),
            Gen.Constant("*"),
            Gen.Constant("+"),
            Gen.Constant("?"),
            Gen.Constant("{"),
            Gen.Constant("[z-a]"),
            Gen.Constant("(?<"),
            Gen.Constant("(?>"),
            Gen.Constant("[\\"),
            Gen.Constant("(?'"),
            Gen.Constant("\\p{InvalidCategory}"),
            Gen.Constant("(abc"));
    }

    /// <summary>
    /// Generates random strings that may or may not be valid regex patterns.
    /// Uses a mix of safe characters and regex metacharacters.
    /// </summary>
    private static Gen<string> GenRandomString()
    {
        var charGen = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'A', 'B', 'C', 'D', 'E', 'F',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
            '.', '*', '+', '?', '^', '$', '|', '-', '_',
            '(', ')', '[', ']', '{', '}',
            '\\', '/', '#', '@', '!', '%', '&', '=', '<', '>');

        return from length in Gen.Choose(0, 12)
               from chars in Gen.ArrayOf(charGen, length)
               select new string(chars);
    }

    /// <summary>
    /// Generates a regex validation scenario: a pattern string paired with
    /// whether it's expected to be valid.
    /// </summary>
    private static Gen<RegexValidationScenario> GenScenario()
    {
        return Gen.Frequency(
            (3, GenValidRegexPattern().Select(p => new RegexValidationScenario(p))),
            (3, GenInvalidRegexPattern().Select(p => new RegexValidationScenario(p))),
            (4, GenRandomString().Select(p => new RegexValidationScenario(p))));
    }

    // ─── Scenario Type ──────────────────────────────────────────────────

    /// <summary>
    /// A regex pattern string to be tested for compile-time validation.
    /// </summary>
#pragma warning disable CA1034
    public record RegexValidationScenario(string Pattern)
    {
        /// <summary>
        /// Whether the pattern is a valid .NET regex (determined at test time).
        /// </summary>
        public bool IsValidRegex => CheckIsValidRegex(Pattern);

        public override string ToString()
        {
            return $"Pattern=\"{Pattern}\" (valid={IsValidRegex})";
        }

        private static bool CheckIsValidRegex(string pattern)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.None);
                return true;
            }
            catch(ArgumentException)
            {
                return false;
            }
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// FsCheck Arbitrary for regex validation scenarios.
    /// </summary>
    public static Arbitrary<RegexValidationScenario> ArbitraryRegexValidationScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Escapes a pattern string for embedding inside backtick delimiters in Mist source.
    /// Backticks become \` and backslashes become \\.
    /// </summary>
    private static string EscapeForBacktickLiteral(string pattern)
    {
        // Order matters: escape backslashes first, then backticks.
        return pattern.Replace("\\", "\\\\").Replace("`", "\\`");
    }

    /// <summary>
    /// Builds a Mist program containing a regex literal with the given pattern.
    /// </summary>
    private static string BuildMistProgram(string pattern)
    {
        var escaped = EscapeForBacktickLiteral(pattern);
        return $"class Program {{ public static void Main() {{ var x = `{escaped}`; }} }}";
    }

    // ─── Property 2 ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Regex literal compile-time validation.
    ///
    /// For any string P:
    /// - If P is a valid .NET regex, compiling `P` as a regex literal SHALL NOT
    ///   emit MIST_TC_REGEX_INVALID.
    /// - If P is NOT a valid .NET regex, compiling `P` as a regex literal SHALL
    ///   emit MIST_TC_REGEX_INVALID.
    ///
    /// **Validates: Requirements 4.6, 5.3, 5.4, 7.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexCompileTimeValidationTests) })]
    public Property RegexLiteral_CompileTimeValidation_MatchesDotNetRegexValidity(
        RegexValidationScenario scenario)
    {
        var source = BuildMistProgram(scenario.Pattern);
        var diagnostics = CompileAndGetDiagnostics(source);

        var regexInvalidDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_REGEX_INVALID")
            .ToImmutableArray();

        if(scenario.IsValidRegex)
        {
            // Valid pattern → no MIST_TC_REGEX_INVALID diagnostic
            return (regexInvalidDiags.Length == 0).ToProperty()
                .Label($"Expected no MIST_TC_REGEX_INVALID for valid pattern \"{scenario.Pattern}\", " +
                       $"but got {regexInvalidDiags.Length} diagnostic(s): " +
                       $"{string.Join("; ", regexInvalidDiags.Select(d => d.Message))}\n" +
                       $"Source:\n{source}");
        }
        else
        {
            // Invalid pattern → at least one MIST_TC_REGEX_INVALID diagnostic
            return (regexInvalidDiags.Length > 0).ToProperty()
                .Label($"Expected MIST_TC_REGEX_INVALID for invalid pattern \"{scenario.Pattern}\", " +
                       $"but got 0 diagnostics.\nSource:\n{source}");
        }
    }
}