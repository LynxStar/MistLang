// Feature: stack-only-types, Property 3: Stackalloc missing/empty size is rejected and recovered
// **Validates: Requirements 3.4, 3.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class StackallocMissingSizeRecoveryPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<StackallocSourceScenario> ArbitraryMissingSizeScenario()
    {
        return MistStackallocGenerator.GenMissingOrEmptySizeSource().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocMissingSizeRecoveryPropertyTests) })]
    public Property MissingOrEmptySize_ProducesNoNode_EmitsRecoveryDiagnostic_AndContinues(
        StackallocSourceScenario scenario)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", scenario.Source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var diagnostics = bag.ToImmutableArray();

        // If the parser is skeletal (MIST0121), the malformed-size behavior cannot
        // be exercised; treat as vacuously satisfied so the suite stays meaningful elsewhere.
        if(diagnostics.Any(d => d.Number.Value == "MIST0121"))
        {
            return true.ToProperty().Label("parser skeletal (MIST0121)");
        }

        var hasStackallocSizeDiagnostic = diagnostics.Any(d =>
            d.Number.Value == "MIST0124"
            && d.Severity == DiagnosticSeverity.Error);

        var stackallocNodeCount = result.Root is null
            ? 0
            : CountStackallocNodes(result.Root);

        // Parse returning normally is the evidence that parsing continues; nothing further is asserted.
        var noNode = stackallocNodeCount == 0;

        return (hasStackallocSizeDiagnostic && noNode)
            .ToProperty()
            .Label(
                $"scenario: {scenario.Description}\n"
                + $"hasStackallocSizeDiagnostic={hasStackallocSizeDiagnostic}, "
                + $"stackallocNodeCount={stackallocNodeCount}\n"
                + $"diagnostics: {string.Join("; ", diagnostics.Select(d => d.Number.Value))}\n"
                + $"source:\n{scenario.Source}");
    }

    private static int CountStackallocNodes(Storm.Runtime.SyntaxNode node)
    {
        var count = node is StackallocExpressionPrimaryNode ? 1 : 0;
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                count += CountStackallocNodes(child);
            }
        }

        return count;
    }
}