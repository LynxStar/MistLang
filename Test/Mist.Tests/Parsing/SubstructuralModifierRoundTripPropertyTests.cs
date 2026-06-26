// Feature: drop-semantics, Property 21: Parsing round-trip
// **Validates: Requirements 1.1, 1.2**

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
using Xunit;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for substructural-modifier round-trip parsing.
///
/// Feature: drop-semantics, Property 21: Parsing round-trip
/// **Validates: Requirements 1.1, 1.2**
///
/// For any valid type declaration containing an <c>affine</c>, <c>linear</c>, or
/// <c>relevant</c> modifier (optionally combined with <c>public</c>/<c>static</c>),
/// parsing then printing the AST SHALL preserve the modifier in the output.
///
/// Runs fully in-process (lex/parse only) and spawns 0 external processes.
/// </summary>
public class SubstructuralModifierRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<SubstructuralModifierSourceScenario> ArbitrarySubstructuralModifierSourceScenario() =>
        MistSubstructuralModifierGenerator.GenSubstructuralTypeDeclaration().ToArbitrary();

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
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 21: Parsing round-trip for substructural modifiers
    //
    // **Validates: Requirements 1.1, 1.2**
    //
    // For any valid type declaration with a substructural modifier, parsing then
    // printing the AST preserves that modifier in the printed output, and the
    // print/parse/print cycle is idempotent.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SubstructuralModifierRoundTripPropertyTests) })]
    public Property RoundTrip_SubstructuralModifierPreserved(SubstructuralModifierSourceScenario scenario)
    {
        // Step 1: Parse the generated source.
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1.
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // The substructural modifier must appear as a standalone keyword in the output.
        var modifierPreserved = ContainsKeyword(printed1, scenario.Modifier);

        return modifierPreserved.ToProperty()
            .Label($"Modifier '{scenario.Modifier}' not preserved.\nSource:\n{scenario.Source}\nPrinted:\n{printed1}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SubstructuralModifierRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintIsIdempotent(SubstructuralModifierSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    // ─── Example-based sanity checks ──────────────────────────────────────

    [Theory]
    [InlineData("affine", "class")]
    [InlineData("linear", "class")]
    [InlineData("relevant", "class")]
    [InlineData("affine", "struct")]
    [InlineData("linear", "struct")]
    [InlineData("relevant", "struct")]
    public void RoundTrip_BareModifier_Preserved(string discipline, string kind)
    {
        var source = $"{discipline} {kind} Foo\n{{\n}}";
        var ast = ParseOrFail(source);
        var printed = MistPrettyPrinter.Print(ast, source);

        Assert.True(ContainsKeyword(printed, discipline),
            $"Modifier '{discipline}' not preserved. Printed:\n{printed}");
    }

    [Theory]
    [InlineData("public linear class ValueTask")]
    [InlineData("public relevant class Task")]
    [InlineData("public affine class AuthToken")]
    [InlineData("public static linear class Foo")]
    public void RoundTrip_CombinedModifiers_Preserved(string header)
    {
        var source = $"{header}\n{{\n}}";
        var ast = ParseOrFail(source);
        var printed = MistPrettyPrinter.Print(ast, source);

        // Every whitespace-separated modifier keyword in the header must survive.
        foreach(var keyword in header.Split(' ').Where(w => w is "public" or "static" or "affine" or "linear" or "relevant"))
        {
            Assert.True(ContainsKeyword(printed, keyword),
                $"Keyword '{keyword}' not preserved. Printed:\n{printed}");
        }
    }

    /// <summary>
    /// Returns true when <paramref name="keyword"/> appears in <paramref name="text"/>
    /// as a standalone word (bounded by non-identifier characters), avoiding false
    /// positives from substrings inside type names.
    /// </summary>
    private static bool ContainsKeyword(string text, string keyword)
    {
        var index = 0;
        while((index = text.IndexOf(keyword, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !IsIdentChar(text[index - 1]);
            var afterPos = index + keyword.Length;
            var after = afterPos >= text.Length || !IsIdentChar(text[afterPos]);
            if(before && after)
            {
                return true;
            }

            index = afterPos;
        }
        return false;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}