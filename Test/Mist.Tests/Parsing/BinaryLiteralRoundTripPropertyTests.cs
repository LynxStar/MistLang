// Feature: binary-literals, Property 5: Pretty printer round-trip for numeric literals
// **Validates: Requirements 5.2, 8.13**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for pretty printer round-trip with numeric literals.
///
/// Property 5: Pretty printer round-trip for numeric literals.
/// For any valid Mist source text containing numeric literals (binary, hex, integer,
/// or float, with or without digit separators), parsing the source then printing the AST
/// then parsing again then printing again SHALL produce identical output on both prints.
///
/// **Validates: Requirements 5.2, 8.13**
/// </summary>
public class BinaryLiteralRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Suffix Constants ────────────────────────────────────────────────

    private static readonly string[] IntSuffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    private static readonly string[] FloatSuffixes =
        ["f", "F", "d", "D", "m", "M"];

    private static readonly string[] FloatSuffixesWithEmpty =
        ["", "f", "F", "d", "D", "m", "M"];

    // ─── Shared Digit Group Generators ───────────────────────────────────

    private static Gen<string> GenDecimalDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Choose(0, 9), length)
            select string.Concat(digits);
    }

    private static Gen<string> GenHexDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Elements(
                '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                'a', 'b', 'c', 'd', 'e', 'f',
                'A', 'B', 'C', 'D', 'E', 'F'), length)
            select new string(digits);
    }

    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    private static Gen<string> GenSeparatorGroups(Gen<string> digitGroupGen)
    {
        return
            from count in Gen.Choose(0, 3)
            from groups in Gen.ArrayOf(digitGroupGen, count)
            select string.Concat(groups.Select(g => "_" + g));
    }

    // ─── Literal Generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a valid binary literal (with or without separators).
    /// </summary>
    private static Gen<string> GenBinaryLiteral()
    {
        return
            from prefix in Gen.Elements("0b", "0B")
            from firstGroup in GenBinaryDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenBinaryDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    /// <summary>
    /// Generates a valid hex literal (with or without separators).
    /// </summary>
    private static Gen<string> GenHexLiteral()
    {
        return
            from prefix in Gen.Elements("0x", "0X")
            from firstGroup in GenHexDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenHexDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    /// <summary>
    /// Generates a valid integer literal (with or without separators).
    /// Starts with 1-9 to avoid matching hex/binary prefixes.
    /// </summary>
    private static Gen<string> GenIntegerLiteral()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 3)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from separatorGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select firstDigit + string.Concat(restDigits) + separatorGroups + suffix;
    }

    /// <summary>
    /// Generates a valid float literal in dot form (with or without separators).
    /// </summary>
    private static Gen<string> GenFloatDotLiteral()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 3)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from intSepGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from fracFirstGroup in GenDecimalDigitGroup()
            from fracSepGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(FloatSuffixesWithEmpty)
            select firstDigit + string.Concat(restDigits) + intSepGroups
                   + "." + fracFirstGroup + fracSepGroups + suffix;
    }

    /// <summary>
    /// Generates a valid float literal in suffix-only form (with or without separators).
    /// </summary>
    private static Gen<string> GenFloatSuffixLiteral()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 3)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from separatorGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(FloatSuffixes)
            select firstDigit + string.Concat(restDigits) + separatorGroups + suffix;
    }

    /// <summary>
    /// Generates any valid numeric literal (binary, hex, integer, or float).
    /// </summary>
    private static Gen<string> GenNumericLiteral()
    {
        return Gen.OneOf(
            GenBinaryLiteral(),
            GenHexLiteral(),
            GenIntegerLiteral(),
            GenFloatDotLiteral(),
            GenFloatSuffixLiteral());
    }

    // ─── Test Scenario ──────────────────────────────────────────────────

    /// <summary>
    /// A complete Mist program containing a numeric literal as a variable initializer.
    /// </summary>
#pragma warning disable CA1034
    public record NumericLiteralRoundTripScenario(string Source, string Literal)
    {
        public override string ToString() => Literal;
    }
#pragma warning restore CA1034

    /// <summary>
    /// Wraps a numeric literal in a minimal valid Mist program.
    /// </summary>
    public static Arbitrary<NumericLiteralRoundTripScenario> ArbitraryNumericLiteralRoundTripScenario()
    {
        var gen =
            from literal in GenNumericLiteral()
            let source = $"class C {{ public static void Main() {{ var x = {literal}; }} }}"
            select new NumericLiteralRoundTripScenario(source, literal);

        return gen.ToArbitrary();
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

    // ─── Property 5 ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: Pretty printer round-trip for numeric literals.
    ///
    /// For any valid Mist source containing a numeric literal (binary, hex, integer,
    /// or float, with or without digit separators):
    ///   parse(source) → print(ast, source) → parse(printed1) → print(ast2, printed1)
    ///   ⇒ printed1 == printed2
    ///
    /// **Validates: Requirements 5.2, 8.13**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(BinaryLiteralRoundTripPropertyTests) })]
    public Property NumericLiteral_RoundTrip_PrintParsePrintIsIdempotent(
        NumericLiteralRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var (root1, errors1) = ParseSource(scenario.Source);

        if(errors1.Count > 0)
            return false.ToProperty()
                .Label($"Initial parse errors: {string.Join("; ", errors1.Select(e => e.Message))}\nSource:\n{scenario.Source}");

        if(root1 is null)
            return false.ToProperty()
                .Label($"Initial parse root was null.\nSource:\n{scenario.Source}");

        // Step 2: Pretty-print the parsed AST (with source text for span-based name extraction)
        var printed1 = MistPrettyPrinter.Print(root1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty()
                .Label($"printed1 was empty.\nSource:\n{scenario.Source}");

        // Step 3: Parse the pretty-printed output
        var (root2, errors2) = ParseSource(printed1);

        if(errors2.Count > 0)
            return false.ToProperty()
                .Label($"Re-parse errors: {string.Join("; ", errors2.Select(e => e.Message))}\nPrinted:\n{printed1}");

        if(root2 is null)
            return false.ToProperty()
                .Label($"Re-parsed root was null.\nPrinted:\n{printed1}");

        // Step 4: Pretty-print the re-parsed AST (with printed1 as source text)
        var printed2 = MistPrettyPrinter.Print(root2, printed1);

        // Step 5: The two printed outputs must be identical
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nLiteral: {scenario.Literal}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}