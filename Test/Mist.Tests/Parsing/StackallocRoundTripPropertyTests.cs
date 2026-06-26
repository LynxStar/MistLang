// Feature: stack-only-types, Property 2: Stackalloc parsing structure and round-trip
// **Validates: Requirements 3.2, 3.6**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for stackalloc expression parsing structure and
/// parse–print–parse round trip.
///
/// **Validates: Requirements 3.2, 3.6**
///
/// For any valid Mist source containing a single <c>stackalloc T[n]</c> expression
/// (in a local-variable initializer, a call argument, or the right-hand side of an
/// assignment), parsing the source SHALL produce exactly one
/// <see cref="StackallocExpressionPrimaryNode"/> with exactly one element-type child
/// and exactly one size-expression child; and pretty-printing the AST back to source
/// then re-parsing SHALL preserve that structure (the parse–print–parse output is stable).
/// </summary>
public class StackallocRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<StackallocSourceScenario> ArbitraryStackallocSourceScenario()
    {
        return MistStackallocGenerator.GenStackallocSource().ToArbitrary();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new Mist.Common.Diagnostics.DiagnosticBag();
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

    /// <summary>Collects every <see cref="StackallocExpressionPrimaryNode"/> reachable from <paramref name="node"/>.</summary>
    private static List<StackallocExpressionPrimaryNode> CollectStackallocNodes(SyntaxNode node)
    {
        var found = new List<StackallocExpressionPrimaryNode>();
        Walk(node);
        return found;

        void Walk(SyntaxNode current)
        {
            if(current is StackallocExpressionPrimaryNode stackallocNode)
            {
                found.Add(stackallocNode);
            }

            foreach(var child in current.Children())
            {
                if(child is not null)
                {
                    Walk(child);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Stackalloc parsing structure and round-trip
    //
    // **Validates: Requirements 3.2, 3.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StackallocRoundTripPropertyTests) })]
    public Property Stackalloc_ParsesToSingleNodeWithTypeAndSizeChildren(StackallocSourceScenario scenario)
    {
        var ast1 = ParseOrFail(scenario.Source);

        var stackallocs = CollectStackallocNodes(ast1);
        if(stackallocs.Count != 1)
        {
            return false.ToProperty().Label(
                $"Expected exactly one StackallocExpression node, found {stackallocs.Count}.\nScenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        var node = stackallocs[0];

        // Exactly one element-type child and one size-expression child.
        var children = node.Children().ToList();
        var hasOneTypeChild = node.Type is not null;
        var hasOneSizeChild = node.Size is not null;
        var exactlyTwoChildren = children.Count == 2;

        return (hasOneTypeChild && hasOneSizeChild && exactlyTwoChildren).ToProperty().Label(
            $"Stackalloc node structure mismatch (typeChild={hasOneTypeChild}, sizeChild={hasOneSizeChild}, childCount={children.Count}).\n" +
            $"Scenario: {scenario.Description}\nSource:\n{scenario.Source}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StackallocRoundTripPropertyTests) })]
    public Property Stackalloc_ParsePrintParsePreservesStructure(StackallocSourceScenario scenario)
    {
        // Step 1: Parse the generated source → AST1
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label($"printed1 was empty.\nSource:\n{scenario.Source}");
        }

        // The printed form must still contain the stackalloc keyword.
        if(!printed1.Contains("stackalloc "))
        {
            return false.ToProperty().Label($"Pretty-printed output dropped the stackalloc keyword:\n{printed1}");
        }

        // Step 3: Re-parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Structure preserved — still exactly one stackalloc node with
        // one type child and one size child, and the printed output is stable.
        var stackallocs2 = CollectStackallocNodes(ast2);
        var structurePreserved =
            stackallocs2.Count == 1 &&
            stackallocs2[0].Type is not null &&
            stackallocs2[0].Size is not null;

        return (structurePreserved && printed1 == printed2).ToProperty().Label(
            $"Round-trip structure/stability mismatch (nodes={stackallocs2.Count}, stable={printed1 == printed2}).\n" +
            $"Scenario: {scenario.Description}\nSource:\n{scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}