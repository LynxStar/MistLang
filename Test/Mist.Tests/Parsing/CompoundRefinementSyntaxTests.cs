using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

[Trait("Tier", "3")]
public sealed class CompoundRefinementSyntaxTests
{
    [Theory]
    [InlineData("type Small = int and > 0;", "int and > 0", 1)]
    [InlineData("type Small = int and > 0 and < 10;", "int and > 0 and < 10", 2)]
    [InlineData("type Small = int and >= 0 and not 5 and <= 9;", "int and >= 0 and not 5 and <= 9", 3)]
    [InlineData("type Either = int and > 0 | string;", "int and > 0 | string", 1)]
    public void ALeadingAndRefinementParsesAsTheCompoundPredicateAndPrintsBack(string source, string written, int operands)
    {
        var first = Compound(Parse(source));
        var printed = MistPrettyPrinter.Print(Parse(source), source);
        var second = Compound(Parse(printed));

        Assert.Contains($"= {written};", printed, StringComparison.Ordinal);
        Assert.Equal(operands, 1 + first.UnaryPatterns.Length);
        Assert.Equal(Operands(first), Operands(second));
    }

    [Fact]
    public void ARefinementWithoutTheLeadingAndStaysTheSimplePredicate()
    {
        var root = Parse("type Small = int > 0 and < 10;");

        Assert.Empty(Descendants(root).OfType<CompoundRefinementPredicateRefinementPredicateNode>());
        _ = Assert.Single(Descendants(root).OfType<SimpleRefinementPredicateRefinementPredicateNode>());
    }

    private static CompoundRefinementPredicateRefinementPredicateNode Compound(Storm.Runtime.SyntaxNode root)
    {
        return Assert.Single(Descendants(root).OfType<CompoundRefinementPredicateRefinementPredicateNode>());
    }

    private static List<string> Operands(CompoundRefinementPredicateRefinementPredicateNode compound)
    {
        return [.. new[] { compound.First }.Concat(compound.UnaryPatterns).Select(Shape)];
    }

    private static string Shape(Storm.Runtime.SyntaxNode node)
    {
        return $"{node.GetType().Name}({string.Join(",", node.Children().Where(child => child is not null).Select(Shape))})";
    }

    private static Storm.Runtime.SyntaxNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var result = new MistParser().Parse(new MistLexer().Lex(new SourceText("probe.mist", source), diagnostics), diagnostics);

        Assert.DoesNotContain(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return Assert.IsAssignableFrom<Storm.Runtime.SyntaxNode>(result.Root);
    }

    private static IEnumerable<Storm.Runtime.SyntaxNode> Descendants(Storm.Runtime.SyntaxNode node)
    {
        foreach(var child in node.Children().Where(child => child is not null))
        {
            yield return child;

            foreach(var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}