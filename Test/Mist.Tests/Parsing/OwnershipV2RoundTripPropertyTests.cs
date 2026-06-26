// Feature: ownership-v2, Property 11: Round-trip parsing
// **Validates: Requirements 17.1, 17.2, 17.3**

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
/// Property-based test for ownership-v2 round-trip parsing (Design Property 11).
///
/// **Validates: Requirements 17.1, 17.2, 17.3**
///
/// For all valid owned type declarations (<c>owned class</c>/<c>owned struct</c>),
/// owned bindings (<c>owned var x = ...</c>, <c>owned T x = ...</c>), and
/// <c>IClonable&lt;T&gt;</c> implementations, the cycle
/// parse → pretty-print → parse yields an equivalent syntax tree. A parse error on
/// the reprinted text counts as a property violation.
///
/// In-process parse/pretty-print only — no process spawn.
/// </summary>
public class OwnershipV2RoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ───────────────────────────────────────────────

    public static Arbitrary<OwnershipV2SourceScenario> ArbitraryOwnershipV2SourceScenario() =>
        MistOwnershipV2Generator.GenOwnershipV2Source().ToArbitrary();

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private readonly record struct ParseAttempt(
        CompilationUnitNode? Root,
        ImmutableArray<string> Errors);

    private static ParseAttempt TryParse(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToImmutableArray();

        return new ParseAttempt(result.Root as CompilationUnitNode, errors);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Property 11: Round-trip parsing
    //
    // **Validates: Requirements 17.1, 17.2, 17.3**
    //
    // parse(source) → pretty-print → parse(reprinted) yields an equivalent tree.
    // Equivalence is established by canonical form: pretty-printing the reparsed tree
    // must reproduce the first printed text exactly. A parse error on the reprinted
    // text counts as a violation.
    // ═══════════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(OwnershipV2RoundTripPropertyTests) })]
    public Property RoundTrip_OwnershipV2Construct_ParsePrintParseIsEquivalent(
        OwnershipV2SourceScenario scenario)
    {
        // Step 1: Parse the generated source. The generator only produces valid v2 source,
        // so an initial parse error is itself a violation.
        var parse1 = TryParse(scenario.Source);
        if(parse1.Errors.Length > 0 || parse1.Root is null)
        {
            return false.ToProperty().Label(
                $"Initial parse failed.\nSource:\n{scenario.Source}\n" +
                $"Errors:\n  {string.Join("\n  ", parse1.Errors)}");
        }

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(parse1.Root, scenario.Source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label($"printed1 was empty.\nSource:\n{scenario.Source}");
        }

        // Step 3: Re-parse the printed text. A parse error here is a violation.
        var parse2 = TryParse(printed1);
        if(parse2.Errors.Length > 0 || parse2.Root is null)
        {
            return false.ToProperty().Label(
                $"Reparse of pretty-printed text failed.\nSource:\n{scenario.Source}\n" +
                $"Printed:\n{printed1}\nErrors:\n  {string.Join("\n  ", parse2.Errors)}");
        }

        // Step 4: Pretty-print AST2 → printed2 and assert tree-equivalence via canonical form.
        var printed2 = MistPrettyPrinter.Print(parse2.Root, printed1);

        return (printed1 == printed2).ToProperty().Label(
            $"Round-trip tree mismatch (parse → print → parse → print not stable).\n" +
            $"Source:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}