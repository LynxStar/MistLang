// Feature: newtypes, Property 8: Pretty-printer round-trip
// **Validates: Requirements 4.1, 4.2**

using System.Collections.Immutable;
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
/// Property-based tests for newtype declaration pretty-print round-trip (idempotence).
///
/// **Validates: Requirements 4.1, 4.2**
///
/// For any valid newtype declaration source string, parsing it and pretty-printing
/// the resulting AST, then parsing and pretty-printing again, SHALL produce identical
/// text output (idempotence).
/// </summary>
public class NewtypeRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario() =>
        MistNewtypeGenerator.GenNewtypeSource().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ─── Property Test ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 8: Pretty-printer round-trip (idempotence).
    ///
    /// For any valid newtype declaration source string, parsing and pretty-printing
    /// the result, then parsing and pretty-printing again, SHALL produce identical output.
    ///
    /// Verifies: print(parse(print(parse(s)))) == print(parse(s))
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeRoundTripPropertyTests) })]
    public Property RoundTrip_Newtype_PrintParseIsIdempotent(NewtypeSourceScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}