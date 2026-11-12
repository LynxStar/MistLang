// Feature: mist-regex-type, Property 1: Pretty-printer round-trip for regex literals
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

public class RegexPrettyPrinterRoundTripTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    /// Avoids unescaped backticks and unescaped backslashes (which need special handling).
    private static Gen<char> GenSafeRegexChar()
    {
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

    /// The lexer recognizes only \\ and \` as escapes; the rest are safe regex escapes.
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

    private static Gen<string> GenRegexAtom()
    {
        return Gen.Frequency(
            (8, GenSafeRegexChar().Select(c => c.ToString())),
            (2, GenEscapeSequence()));
    }

    private static Gen<string> GenRegexPattern()
    {
        return Gen.Frequency(
            (1, Gen.Constant("")),  // empty pattern
            (4, from length in Gen.Choose(1, 8)
                from atoms in Gen.ArrayOf(GenRegexAtom(), length)
                select string.Concat(atoms)),
            (3, GenSimpleRegexPattern()));
    }

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

    private static Gen<string> GenRegexExpressionSource()
    {
        return from pattern in GenRegexPattern()
               select $"class C {{ public static void Main() {{ var x = `{pattern}`; }} }}";
    }

    private static Gen<string> GenRegexPatternMatchSource()
    {
        return from pattern in GenRegexPattern()
               select $"class C {{ public static void Main() {{ string s = \"hello\"; if (s is `{pattern}`) {{ }} }} }}";
    }

    private static Gen<string> GenRegexTypeAnnotationSource()
    {
        return from pattern in GenRegexPattern()
               select $"class C {{ public static void Main() {{ regex x = `{pattern}`; }} }}";
    }

    private static Gen<RegexRoundTripScenario> GenRegexRoundTripScenario()
    {
        return Gen.OneOf(
            GenRegexExpressionSource().Select(s => new RegexRoundTripScenario(s, "expression")),
            GenRegexPatternMatchSource().Select(s => new RegexRoundTripScenario(s, "pattern")),
            GenRegexTypeAnnotationSource().Select(s => new RegexRoundTripScenario(s, "type-annotation")));
    }

#pragma warning disable CA1034
    public record RegexRoundTripScenario(string Source, string Position)
    {
        public override string ToString()
        {
            return $"[{Position}] {Source}";
        }
    }
#pragma warning restore CA1034

    public static Arbitrary<RegexRoundTripScenario> ArbitraryRegexRoundTripScenario()
    {
        return GenRegexRoundTripScenario().ToArbitrary();
    }

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

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexPrettyPrinterRoundTripTests) })]
    public Property RegexLiteral_RoundTrip_PrintParsePrintIsIdempotent(
        RegexRoundTripScenario scenario)
    {
        var (root1, errors1) = ParseSource(scenario.Source);

        if(errors1.Count > 0)
        {
            return false.ToProperty()
                .Label($"Initial parse errors: {string.Join("; ", errors1.Select(e => e.Message))}\nSource:\n{scenario.Source}");
        }

        if(root1 is null)
        {
            return false.ToProperty()
                .Label($"Initial parse root was null.\nSource:\n{scenario.Source}");
        }

        var printed1 = MistPrettyPrinter.Print(root1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty()
                .Label($"printed1 was empty.\nSource:\n{scenario.Source}");
        }

        var (root2, errors2) = ParseSource(printed1);

        if(errors2.Count > 0)
        {
            return false.ToProperty()
                .Label($"Re-parse errors: {string.Join("; ", errors2.Select(e => e.Message))}\nPrinted:\n{printed1}");
        }

        if(root2 is null)
        {
            return false.ToProperty()
                .Label($"Re-parsed root was null.\nPrinted:\n{printed1}");
        }

        var printed2 = MistPrettyPrinter.Print(root2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nPosition: {scenario.Position}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}