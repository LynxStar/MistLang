// Feature: structured-context, Property 1: Pretty-printer round-trip idempotence
//
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.5, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 13.1**
//
// For any valid Mist AST containing structured context constructs (with-var
// declarations, with-typed declarations, with-discard declarations, with-push
// statements, with-block statements, and method with-clauses on methods,
// constructors, and abstract type methods),
// print(parse(print(ast))) SHALL equal print(ast).
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for structured context pretty-printer round-trip idempotence.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.5, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 13.1**
///
/// For any valid Mist AST containing structured context constructs,
/// pretty-printing → parsing → pretty-printing SHALL produce identical output.
/// </summary>
public class ContextRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<ContextRoundTripScenario> ArbitraryContextRoundTripScenario()
    {
        return MistContextRoundTripGenerator.GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Pretty-printer round-trip idempotence
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8,
    //              2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.5, 11.1, 11.2, 11.3,
    //              11.4, 11.5, 11.6, 11.7, 13.1**
    //
    // For any valid Mist source program containing structured context
    // constructs, parsing and pretty-printing the result, then parsing and
    // pretty-printing again, SHALL produce identical output (idempotence).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ContextRoundTripPropertyTests) })]
    public Property RoundTrip_ContextConstructs_PrintParseIsIdempotent(ContextRoundTripScenario scenario)
    {
        var source = scenario.Source;

        // Parse₁
        var sourceText1 = new Mist.Common.Text.SourceText("test.mist", source);
        var bag1 = new DiagnosticBag();
        var tokens1 = Lexer.Lex(sourceText1, bag1);
        var result1 = Parser.Parse(tokens1, bag1);

        var errors1 = bag1.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors1.Count > 0)
        {
            return false.ToProperty()
                .Label($"First-parse errors for {scenario}:\n  " +
                       $"{string.Join("\n  ", errors1.Select(e => e.Message))}\n" +
                       $"Source:\n{source}");
        }

        if(result1.Root is not CompilationUnitNode ast1)
        {
            return false.ToProperty()
                .Label($"First-parse root was null for {scenario}\nSource:\n{source}");
        }

        // Print₁
        var printed1 = MistPrettyPrinter.Print(ast1, source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty()
                .Label($"printed1 was empty for {scenario}");
        }

        // Sanity: the printed output must contain the `with` keyword
        if(!printed1.Contains("with"))
        {
            return false.ToProperty()
                .Label($"printed1 lost the `with` keyword for {scenario}\n" +
                       $"Source:\n{source}\nPrinted:\n{printed1}");
        }

        // Parse₂
        var sourceText2 = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag2 = new DiagnosticBag();
        var tokens2 = Lexer.Lex(sourceText2, bag2);
        var result2 = Parser.Parse(tokens2, bag2);

        var errors2 = bag2.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors2.Count > 0)
        {
            return false.ToProperty()
                .Label($"Reparse errors for {scenario}:\n  " +
                       $"{string.Join("\n  ", errors2.Select(e => e.Message))}\n" +
                       $"Printed:\n{printed1}");
        }

        if(result2.Root is not CompilationUnitNode ast2)
        {
            return false.ToProperty()
                .Label($"Reparse root was null for {scenario}\nPrinted:\n{printed1}");
        }

        // Print₂
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Assert: Print₁ == Print₂ (idempotence)
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch for {scenario}\n" +
                   $"Printed1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}