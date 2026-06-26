// Feature: ref-parameters, Property 1: Pretty Printer Round-Trip
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for ref-annotated pretty printer round-trip.
///
/// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**
///
/// For any valid Mist source text containing ref-annotated declarations (ref parameters,
/// ref returns, ref locals, ref arguments, return ref statements), parsing the source and
/// pretty-printing the resulting AST, then parsing the output and pretty-printing again,
/// SHALL produce character-for-character identical output on both emissions.
/// </summary>
public class RefParameterRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<RefParameterRoundTripScenario> ArbitraryRefParameterRoundTripScenario()
    {
        return MistRefParameterRoundTripGenerator.GenScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Pretty Printer Round-Trip
    //
    // **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**
    //
    // For any valid ref-annotated Mist source fragment, parsing and
    // pretty-printing the result, then parsing and pretty-printing again,
    // SHALL produce identical output (idempotence / round-trip stability).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefParameterRoundTripPropertyTests) })]
    public Property RoundTrip_RefAnnotatedSource_PrintParseIsIdempotent(RefParameterRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}