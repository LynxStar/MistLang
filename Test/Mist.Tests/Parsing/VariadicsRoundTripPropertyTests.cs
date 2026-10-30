// Feature: variadics — Property 1: Print/parse round-trip (Task 2.2)
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**

using System.Collections.Immutable;
using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Parsing;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based test for the variadics pretty-printer print/parse round-trip
/// (Property 1 of the variadics design).
///
/// <para>
/// <b>Property 1: Print/parse round-trip.</b> For every valid AST that contains
/// variadic surface forms — pack declarations in the <c>params</c> and
/// <c>Type[]:</c> / <c>Type[N]:</c> spellings, <c>params TArgs args</c> and
/// homogeneous <c>params T[]</c> value parameters, plain <c>(TArgs...)</c> tuple
/// parameters, spreads (<c>TArgs...</c> and <c>args...</c>), and both flat and
/// bracketed argument lists — parsing the text produced by the pretty-printer
/// yields an AST that is structurally equal to the original: identical node kinds
/// and nesting, declared identifiers, element types, fixed-size domain lengths, and
/// modifiers, and the same flat-versus-bracketed choice for each argument list,
/// disregarding source positions and formatting trivia.
/// </para>
///
/// <para>
/// The round-trip is exercised entirely <b>in-process</b> with <b>zero</b> external
/// process spawns: parse source → AST1, pretty-print AST1 → text, re-parse text →
/// AST2, then assert AST1 and AST2 are structurally equal. Scenarios come from the
/// shared <see cref="MistVariadicsGenerator"/> and parsing uses the shared
/// <see cref="VariadicsCompilerFixture"/> (both from task 1.3).
/// </para>
///
/// <para>
/// Structural equality is checked by <see cref="AstStructurallyEqual"/>, a focused
/// comparison over the Storm <see cref="SyntaxNode"/> tree that compares (1) the node
/// kind (runtime type) at each position, (2) every token-valued property by kind and
/// lexeme — capturing declared identifiers, primitive element-type keywords,
/// fixed-size domain lengths, and modifier presence — and (3) the ordered child
/// nodes recursively. Source positions and trivia (spans, parent links, hidden
/// leading/trailing tokens, and node origins declared on the <see cref="SyntaxNode"/>
/// base) are deliberately excluded.
/// </para>
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**
/// </summary>
public class VariadicsRoundTripPropertyTests
{
    /// <summary>Arbitrary registration for the FsCheck property below.</summary>
    public static Arbitrary<VariadicSurfaceScenario> VariadicScenarios()
    {
        return MistVariadicsGenerator.ArbitraryVariadicSurfaceScenario();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Print/parse round-trip (structural equality).
    //
    // **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsRoundTripPropertyTests) }, MaxTest = 100)]
    public Property RoundTrip_VariadicSurfaceForms_IsStructurallyStable(VariadicSurfaceScenario scenario)
    {
        // Step 1: parse the generated source into the original AST (AST1).
        var outcome1 = VariadicsCompilerFixture.Parse(scenario.Source);
        if(!VariadicsCompilerFixture.ParsedCleanly(outcome1) || outcome1.Root is null)
        {
            // The generators only emit grammar-valid source (guaranteed by the task
            // 1.3 smoke test), so a parse failure here is unexpected — surface it.
            return false.ToProperty().Label(
                $"Generated source did not parse cleanly:\n{scenario.Source}\n"
                + DescribeDiagnostics(outcome1.Diagnostics));
        }

        var ast1 = outcome1.Root;

        // Step 2: pretty-print AST1 (using the original source for qualified-name
        // fidelity), then re-parse the printed text into AST2 — all in-process.
        var printed = MistPrettyPrinter.Print(ast1, scenario.Source);
        var outcome2 = VariadicsCompilerFixture.Parse(printed);
        if(!VariadicsCompilerFixture.ParsedCleanly(outcome2) || outcome2.Root is null)
        {
            return false.ToProperty().Label(
                "Pretty-printed text did not parse cleanly (round-trip gap).\n"
                + $"Source:\n{scenario.Source}\nPrinted:\n{printed}\n"
                + DescribeDiagnostics(outcome2.Diagnostics));
        }

        var ast2 = outcome2.Root;

        // Step 3: AST1 and AST2 must be structurally equal, ignoring positions/trivia.
        var equal = AstStructurallyEqual(ast1, ast2, out var diff);
        return equal.ToProperty().Label(
            $"Round-trip AST mismatch: {diff}\nSource:\n{scenario.Source}\nPrinted:\n{printed}");
    }

    // ─── Structural equality over the AST (ignoring positions/trivia) ────────

    /// <summary>
    /// Structurally compares two AST subtrees, ignoring source positions and trivia.
    /// Two nodes are structurally equal when they have the same runtime type (node
    /// kind), all their token-valued properties match by token kind and lexeme, and
    /// their ordered child nodes are pairwise structurally equal.
    /// </summary>
    /// <param name="diff">
    /// On failure, a human-readable description of the first difference found.
    /// </param>
    private static bool AstStructurallyEqual(SyntaxNode? a, SyntaxNode? b, out string diff)
    {
        // Children() may yield null entries for absent non-nullable child slots, so
        // both sides must agree on presence before comparing kinds.
        if(a is null && b is null)
        {
            diff = "";
            return true;
        }

        if(a is null || b is null)
        {
            diff = $"node presence differs ({(a is null ? "null" : a.GetType().Name)} "
                + $"vs {(b is null ? "null" : b.GetType().Name)})";
            return false;
        }

        // (1) Node kind: the runtime type captures modifiers-as-nodes and the
        //     flat-versus-bracketed argument-list choice (distinct node types).
        if(a.GetType() != b.GetType())
        {
            diff = $"node kind differs ({a.GetType().Name} vs {b.GetType().Name})";
            return false;
        }

        // (2) Token-valued properties: declared identifiers, primitive element-type
        //     lexemes, the fixed-size domain length N, and modifier presence all live
        //     in Token / Token? / ImmutableArray<Token> properties. Properties
        //     declared on the SyntaxNode base (Span, Parent, Origin, and the hidden
        //     leading/trailing trivia arrays) are skipped as positional/trivia.
        foreach(var prop in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if(prop.DeclaringType == typeof(SyntaxNode) || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propType = prop.PropertyType;
            if(propType == typeof(Token))
            {
                var ta = (Token)prop.GetValue(a)!;
                var tb = (Token)prop.GetValue(b)!;
                if(!TokensEqual(ta, tb))
                {
                    diff = $"{a.GetType().Name}.{prop.Name} token differs ('{ta.Lexeme}' vs '{tb.Lexeme}')";
                    return false;
                }
            }
            else if(propType == typeof(Token?))
            {
                var ta = (Token?)prop.GetValue(a);
                var tb = (Token?)prop.GetValue(b);
                if(ta.HasValue != tb.HasValue)
                {
                    diff = $"{a.GetType().Name}.{prop.Name} optional-token presence differs "
                        + $"({(ta.HasValue ? "set" : "unset")} vs {(tb.HasValue ? "set" : "unset")})";
                    return false;
                }

                if(ta.HasValue && !TokensEqual(ta.Value, tb!.Value))
                {
                    diff = $"{a.GetType().Name}.{prop.Name} optional token differs "
                        + $"('{ta.Value.Lexeme}' vs '{tb!.Value.Lexeme}')";
                    return false;
                }
            }
            else if(propType == typeof(ImmutableArray<Token>))
            {
                var la = (ImmutableArray<Token>)prop.GetValue(a)!;
                var lb = (ImmutableArray<Token>)prop.GetValue(b)!;
                if(!TokenListsEqual(la, lb))
                {
                    diff = $"{a.GetType().Name}.{prop.Name} token list differs";
                    return false;
                }
            }

            // Node / nullable-node / node-list properties are compared via Children()
            // below (which enumerates them in source order), so they are not handled
            // here.
        }

        // (3) Child nodes, in source order: captures nesting and presence/absence of
        //     optional children (Children() omits null children).
        var childrenA = a.Children().ToList();
        var childrenB = b.Children().ToList();
        if(childrenA.Count != childrenB.Count)
        {
            diff = $"{a.GetType().Name} child count differs ({childrenA.Count} vs {childrenB.Count})";
            return false;
        }

        for(var i = 0; i < childrenA.Count; i++)
        {
            if(!AstStructurallyEqual(childrenA[i], childrenB[i], out diff))
            {
                return false;
            }
        }

        diff = "";
        return true;
    }

    /// <summary>
    /// Two tokens are equal for round-trip purposes when they have the same token
    /// kind and lexeme. Span, line/column position, and channel are ignored.
    /// </summary>
    private static bool TokensEqual(Token a, Token b)
    {
        return a.Kind == b.Kind && a.Lexeme == b.Lexeme;
    }

    private static bool TokenListsEqual(ImmutableArray<Token> a, ImmutableArray<Token> b)
    {
        if(a.IsDefaultOrEmpty && b.IsDefaultOrEmpty)
        {
            return true;
        }

        if(a.IsDefaultOrEmpty != b.IsDefaultOrEmpty || a.Length != b.Length)
        {
            return false;
        }

        for(var i = 0; i < a.Length; i++)
        {
            if(!TokensEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        var lines = diagnostics.Select(d => $"{d.Number.Value} [{d.Stage}] {d.Message}").ToList();
        return lines.Count == 0 ? "(no diagnostics)" : string.Join("; ", lines);
    }
}