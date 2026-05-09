// Feature: mist-regex-type, Property 1: Pretty-printer round-trip for regex literals
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for pretty-printer round-trip with regex literals.
///
/// Property 1: Pretty-printer round-trip for regex literals.
/// For any valid Mist source text containing regex literal expressions and/or regex
/// constant patterns, parsing the source then printing the AST then parsing again
/// then printing again SHALL produce identical output on both prints.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**
/// </summary>
public class RegexPrettyPrinterRoundTripTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Regex Pattern Generators ────────────────────────────────────────

    /// <summary>
    /// Generates a safe character that can appear inside a backtick-delimited regex literal.
    /// Avoids unescaped backticks and unescaped backslashes (which need special handling).
    /// </summary>
    private static Gen<char> GenSafeRegexChar()
    {
        // ASCII printable characters excluding backtick (96) and backslash (92)
        return Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
            'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
            'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
            '.', '*', '+', '?', '^', '$', '|', '-', '_', ' ',
            '(', ')', '[', ']', '{', '}', ',', ':', ';', '!', '@', '#', '%',
            '&', '=', '<', '>', '/', '~');
    }

    /// <summary>
    /// Generates a valid escape sequence for use inside a regex literal.
    /// These are the two-character sequences recognized by the lexer: \\ and \`
    /// Plus common regex escape sequences that are safe.
    /// </summary>
    private static Gen<string> GenEscapeSequence()
    {
        return Gen.Elements(
            @"\\",  // escaped backslash
            @"\`",  // escaped backtick
            @"\d",  // digit class
            @"\w",  // word class
            @"\s",  // whitespace class
            @"\D",  // non-digit class
            @"\W",  // non-word class
            @"\S",  // non-whitespace class
            @"\n",  // newline
            @"\t",  // tab
            @"\.",  // escaped dot
            @"\*",  // escaped star
            @"\+",  // escaped plus
            @"\?",  // escaped question mark
            @"\(",  // escaped open paren
            @"\)",  // escaped close paren
            @"\[",  // escaped open bracket
            @"\]"); // escaped close bracket
    }

    /// <summary>
    /// Generates a single "atom" of a regex pattern — either a safe character or an escape sequence.
    /// </summary>
    private static Gen<string> GenRegexAtom()
    {
        return Gen.Frequency(
            (8, GenSafeRegexChar().Select(c => c.ToString())),
            (2, GenEscapeSequence()));
    }

    /// <summary>
    /// Generates a valid regex pattern string (the content between backticks).
    /// Patterns are kept short to avoid overly complex inputs.
    /// </summary>
    private static Gen<string> GenRegexPattern()
    {
        return Gen.Frequency(
            (1, Gen.Constant("")),  // empty pattern
            (4, from length in Gen.Choose(1, 8)
                from atoms in Gen.ArrayOf(GenRegexAtom(), length)
                select string.Concat(atoms)),
            (3, GenSimpleRegexPattern()));
    }

    /// <summary>
    /// Generates common simple regex patterns that are known to be valid .NET regex.
    /// </summary>
    private static Gen<string> GenSimpleRegexPattern()
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
            "test");
    }

    /// <summary>
    /// Generates a backtick-delimited regex literal string (including the backtick delimiters).
    /// </summary>
    private static Gen<string> GenRegexLiteral()
    {
        return GenRegexPattern().Select(p => $"`{p}`");
    }

    // ─── Source Program Generators ───────────────────────────────────────

    /// <summary>
    /// Generates a Mist source program with a regex literal in expression position:
    /// <c>var x = `pattern`;</c>
    /// </summary>
    private static Gen<string> GenRegexExpressionSource()
    {
        return from pattern in GenRegexPattern()
               select $"class C {{ public static void Main() {{ var x = `{pattern}`; }} }}";
    }

    /// <summary>
    /// Generates a Mist source program with a regex literal in pattern position:
    /// <c>if (s is `pattern`) { }</c>
    /// </summary>
    private static Gen<string> GenRegexPatternMatchSource()
    {
        return from pattern in GenRegexPattern()
               select $"class C {{ public static void Main() {{ string s = \"hello\"; if (s is `{pattern}`) {{ }} }} }}";
    }

    /// <summary>
    /// Generates a Mist source program with the <c>regex</c> keyword in type annotation position:
    /// <c>regex x = `pattern`;</c>
    /// </summary>
    private static Gen<string> GenRegexTypeAnnotationSource()
    {
        return from pattern in GenRegexPattern()
               select $"class C {{ public static void Main() {{ regex x = `{pattern}`; }} }}";
    }

    /// <summary>
    /// Generates a random regex round-trip scenario from any of the three positions.
    /// </summary>
    private static Gen<RegexRoundTripScenario> GenRegexRoundTripScenario()
    {
        return Gen.OneOf(
            GenRegexExpressionSource().Select(s => new RegexRoundTripScenario(s, "expression")),
            GenRegexPatternMatchSource().Select(s => new RegexRoundTripScenario(s, "pattern")),
            GenRegexTypeAnnotationSource().Select(s => new RegexRoundTripScenario(s, "type-annotation")));
    }

    // ─── Test Scenario ──────────────────────────────────────────────────

    /// <summary>
    /// A complete Mist program containing a regex literal for round-trip testing.
    /// </summary>
#pragma warning disable CA1034
    public record RegexRoundTripScenario(string Source, string Position)
    {
        public override string ToString() => $"[{Position}] {Source}";
    }
#pragma warning restore CA1034

    /// <summary>
    /// FsCheck Arbitrary for regex round-trip scenarios.
    /// </summary>
    public static Arbitrary<RegexRoundTripScenario> ArbitraryRegexRoundTripScenario()
    {
        return GenRegexRoundTripScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string and returns the compilation unit and any errors.
    /// </summary>
    private static (CompilationUnitNode? Root, List<Diagnostic> Errors) ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (result.Root as CompilationUnitNode, errors);
    }

    // ─── Property 1 ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Pretty-printer round-trip for regex literals.
    ///
    /// For any valid Mist source containing a regex literal (in expression position,
    /// pattern position, or with regex type annotation):
    ///   parse(source) → print(ast, source) → parse(printed1) → print(ast2, printed1)
    ///   ⇒ printed1 == printed2
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexPrettyPrinterRoundTripTests) })]
    public Property RegexLiteral_RoundTrip_PrintParsePrintIsIdempotent(
        RegexRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var (root1, errors1) = ParseSource(scenario.Source);

        if (errors1.Count > 0)
            return false.ToProperty()
                .Label($"Initial parse errors: {string.Join("; ", errors1.Select(e => e.Message))}\nSource:\n{scenario.Source}");

        if (root1 is null)
            return false.ToProperty()
                .Label($"Initial parse root was null.\nSource:\n{scenario.Source}");

        // Step 2: Pretty-print the parsed AST (with source text for span-based name extraction)
        var printed1 = MistPrettyPrinter.Print(root1, scenario.Source);

        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty()
                .Label($"printed1 was empty.\nSource:\n{scenario.Source}");

        // Step 3: Parse the pretty-printed output
        var (root2, errors2) = ParseSource(printed1);

        if (errors2.Count > 0)
            return false.ToProperty()
                .Label($"Re-parse errors: {string.Join("; ", errors2.Select(e => e.Message))}\nPrinted:\n{printed1}");

        if (root2 is null)
            return false.ToProperty()
                .Label($"Re-parsed root was null.\nPrinted:\n{printed1}");

        // Step 4: Pretty-print the re-parsed AST (with printed1 as source text)
        var printed2 = MistPrettyPrinter.Print(root2, printed1);

        // Step 5: The two printed outputs must be identical
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nPosition: {scenario.Position}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}
