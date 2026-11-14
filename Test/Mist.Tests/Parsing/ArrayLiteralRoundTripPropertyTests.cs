// Feature: array-literals, Property 9: Pretty-print round trip
// **Validates: Requirements 9.1, 9.2, 9.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

public class ArrayLiteralRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static Gen<string> GenNonNegativeInt()
    {
        return Gen.Choose(0, 9999).Select(n => n.ToString());
    }

    private static Gen<string> GenArrayLiteral()
    {
        var nonEmpty = from count in Gen.Choose(1, 10)
                       from elements in Gen.ArrayOf(GenNonNegativeInt(), count)
                       select "[" + string.Join(", ", elements) + "]";

        var empty = Gen.Constant("[]");

        return Gen.Frequency(
            (9, nonEmpty),
            (1, empty));
    }

#pragma warning disable CA1034
    public record ArrayLiteralRoundTripScenario(string Source, string ArrayLiteral)
    {
        public override string ToString()
        {
            return ArrayLiteral;
        }
    }
#pragma warning restore CA1034

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
        Arbitrary = new[] { typeof(ArrayLiteralRoundTripPropertyTests) })]
    public Property ArrayLiteral_RoundTrip_PrintParsePrintIsIdempotent(
        ArrayLiteralRoundTripScenario scenario)
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
            .Label($"Mismatch.\nArrayLiteral: {scenario.ArrayLiteral}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}