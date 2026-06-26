// Feature: array-literals, Property 9: Pretty-print round trip
// **Validates: Requirements 9.1, 9.2, 9.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for pretty-print round trip with array literals.
///
/// Property 9: Pretty-print round trip.
/// For any valid Mist source text containing an array literal, parsing then
/// pretty-printing then parsing again then pretty-printing again SHALL produce
/// identical output on both prints (round-trip stability).
///
/// Uses in-process MistLexer/MistParser/MistPrettyPrinter — no process spawning.
///
/// **Validates: Requirements 9.1, 9.2, 9.3**
/// </summary>
public class ArrayLiteralRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random non-negative integer as a string literal.
    /// </summary>
    private static Gen<string> GenNonNegativeInt()
    {
        return Gen.Choose(0, 9999).Select(n => n.ToString());
    }

    /// <summary>
    /// Generates a random array literal with 1-10 non-negative integer elements.
    /// </summary>
    private static Gen<string> GenArrayLiteral()
    {
        var nonEmpty = from count in Gen.Choose(1, 10)
                       from elements in Gen.ArrayOf(GenNonNegativeInt(), count)
                       select "[" + string.Join(", ", elements) + "]";

        // Include empty array literal [] in the generated set
        var empty = Gen.Constant("[]");

        return Gen.Frequency(
            (9, nonEmpty),
            (1, empty));
    }

    // ─── Test Scenario ──────────────────────────────────────────────────

    /// <summary>
    /// A complete Mist program containing an array literal as a variable initializer.
    /// </summary>
#pragma warning disable CA1034
    public record ArrayLiteralRoundTripScenario(string Source, string ArrayLiteral)
    {
        public override string ToString() => ArrayLiteral;
    }
#pragma warning restore CA1034

    /// <summary>
    /// Wraps an array literal in a minimal valid Mist class/method context.
    /// For empty array literals, uses a typed variable declaration.
    /// </summary>
    public static Arbitrary<ArrayLiteralRoundTripScenario> ArbitraryArrayLiteralRoundTripScenario()
    {
        var gen =
            from arrayLiteral in GenArrayLiteral()
            let source = arrayLiteral == "[]"
                ? $"class C {{ public static void Main() {{ int[] x = {arrayLiteral}; }} }}"
                : $"class C {{ public static void Main() {{ var x = {arrayLiteral}; }} }}"
            select new ArrayLiteralRoundTripScenario(source, arrayLiteral);

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

    // ─── Property 9 ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 9: Pretty-print round trip.
    ///
    /// For any valid Mist source containing an array literal of non-negative integers:
    ///   parse(source) → print(ast, source) → parse(printed1) → print(ast2, printed1)
    ///   ⇒ printed1 == printed2
    ///
    /// **Validates: Requirements 9.1, 9.2, 9.3**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralRoundTripPropertyTests) })]
    public Property ArrayLiteral_RoundTrip_PrintParsePrintIsIdempotent(
        ArrayLiteralRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var (root1, errors1) = ParseSource(scenario.Source);

        if(errors1.Count > 0)
            return false.ToProperty()
                .Label($"Initial parse errors: {string.Join("; ", errors1.Select(e => e.Message))}\nSource:\n{scenario.Source}");

        if(root1 is null)
            return false.ToProperty()
                .Label($"Initial parse root was null.\nSource:\n{scenario.Source}");

        // Step 2: Pretty-print the parsed AST
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

        // Step 4: Pretty-print the re-parsed AST
        var printed2 = MistPrettyPrinter.Print(root2, printed1);

        // Step 5: The two printed outputs must be identical (round-trip stability)
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nArrayLiteral: {scenario.ArrayLiteral}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}