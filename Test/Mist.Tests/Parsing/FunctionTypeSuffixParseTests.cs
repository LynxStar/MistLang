// Tier 1 (Unit): the parser alone. A `func<...>` type reference carries an array rank and an optional
// suffix of its own, wherever a type is written, and the parenthesized spelling keeps the parse it had.

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

public sealed class FunctionTypeSuffixParseTests
{
    [Theory]
    [InlineData("class Probe { private func<int, int>[] held; }")]
    [InlineData("class Probe { public func<int, int>[] Held { get; set; } }")]
    [InlineData("class Probe { void Run(func<int, int>[] steps) { } }")]
    [InlineData("class Probe { func<int, int>[] Make() { return steps; } }")]
    [InlineData("class Probe { void Run() { func<int, int>[] steps = [f]; } }")]
    [InlineData("class Probe { void Run() { func<int, int>[] steps; } }")]
    [InlineData("class Probe { void Run(List<func<int, int>[]> stages) { } }")]
    [InlineData("class Probe { void Run(func<int, int>[] | string value) { } }")]
    [InlineData("class Probe { void Run((func<int, int>[] steps, int count) pair) { } }")]
    [InlineData("class Probe { void Run() { foreach(func<int, int>[] stage in stages) { } } }")]
    [InlineData("class Probe { void Run() { var steps = (func<int, int>[])raw; } }")]
    [InlineData("type Stages = func<int, int>[];")]
    public void AnArrayRankAfterAFunctionTypeBelongsToTheFunctionType(string source)
    {
        var funcRef = Assert.Single(Descendants(Parse(source)).OfType<FuncSingleTypeRefSingleTypeRefNode>());

        _ = Assert.IsType<UnsizedArrayRankArrayRankSuffixNode>(funcRef.ArrayRank);
        Assert.Null(funcRef.Optional);
    }

    [Theory]
    [InlineData("class Probe { private func<int, int>? held; }")]
    [InlineData("class Probe { public func<int, int>? Held { get; set; } }")]
    [InlineData("class Probe { void Run(func<int, int>? step) { } }")]
    [InlineData("class Probe { func<int, int>? Make() { return step; } }")]
    [InlineData("class Probe { void Run() { func<int, int>? step = f; } }")]
    [InlineData("class Probe { void Run(List<func<int, int>?> steps) { } }")]
    [InlineData("class Probe { void Run((func<int, int>? step, int count) pair) { } }")]
    [InlineData("type MaybeStep = func<int, int>?;")]
    public void AnOptionalSuffixAfterAFunctionTypeBelongsToTheFunctionType(string source)
    {
        var funcRef = Assert.Single(Descendants(Parse(source)).OfType<FuncSingleTypeRefSingleTypeRefNode>());

        Assert.Null(funcRef.ArrayRank);
        _ = Assert.NotNull(funcRef.Optional);
    }

    [Fact]
    public void ASizedRankAfterAFunctionTypeKeepsItsLength()
    {
        var funcRef = Assert.Single(
            Descendants(Parse("class Probe { void Run(func<int, int>[2] steps) { } }")).OfType<FuncSingleTypeRefSingleTypeRefNode>());

        Assert.Equal("2", Assert.IsType<SizedArrayRankArrayRankSuffixNode>(funcRef.ArrayRank).Size.Lexeme);
    }

    [Fact]
    public void AnArrayOfFunctionsIsItselfOptionalWhenBothSuffixesAreWritten()
    {
        var funcRef = Assert.Single(
            Descendants(Parse("class Probe { void Run(func<int, int>[]? steps) { } }")).OfType<FuncSingleTypeRefSingleTypeRefNode>());

        _ = Assert.IsType<UnsizedArrayRankArrayRankSuffixNode>(funcRef.ArrayRank);
        _ = Assert.NotNull(funcRef.Optional);
    }

    [Fact]
    public void AFunctionTypeWithNoSuffixTakesNeitherFromWhatFollowsIt()
    {
        var funcRef = Assert.Single(
            Descendants(Parse("class Probe { void Run() { func<int, int> step = steps[0]; } }")).OfType<FuncSingleTypeRefSingleTypeRefNode>());

        Assert.Null(funcRef.ArrayRank);
        Assert.Null(funcRef.Optional);
    }

    [Fact]
    public void TheParenthesizedSpellingStillCarriesTheRankOnTheParentheses()
    {
        var nodes = Descendants(Parse("class Probe { void Run((func<int, int>)[] steps) { } }")).ToList();

        var parenRef = Assert.Single(nodes.OfType<ParenthesizedTypeRefSingleTypeRefNode>());
        _ = Assert.IsType<UnsizedArrayRankArrayRankSuffixNode>(parenRef.ArrayRank);
        Assert.Null(Assert.Single(nodes.OfType<FuncSingleTypeRefSingleTypeRefNode>()).ArrayRank);
    }

    [Theory]
    [InlineData("class Probe { void Run(func<int, int>[] steps) { } }")]
    [InlineData("class Probe { void Run(func<int, int>[2] steps) { } }")]
    [InlineData("class Probe { void Run(func<int, int>? step) { } }")]
    [InlineData("class Probe { void Run(func<int, int>[]? steps) { } }")]
    public void PrintingASuffixedFunctionTypeAndParsingItBackIsTheSameTree(string source)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        var firstRef = Assert.Single(Descendants(first).OfType<FuncSingleTypeRefSingleTypeRefNode>());
        var secondRef = Assert.Single(Descendants(second).OfType<FuncSingleTypeRefSingleTypeRefNode>());

        Assert.Equal(Shape(first), Shape(second));
        Assert.Equal(firstRef.ArrayRank?.GetType(), secondRef.ArrayRank?.GetType());
        Assert.Equal(firstRef.Optional is not null, secondRef.Optional is not null);
    }

    private static string Shape(Storm.Runtime.SyntaxNode node)
    {
        return $"{node.GetType().Name}({string.Join(",", node.Children().Where(child => child is not null).Select(Shape))})";
    }

    private static Storm.Runtime.SyntaxNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var text = new Mist.Common.Source.SourceText("probe.mist", source);
        var result = new Mist.Parsing.MistParser().Parse(new Mist.Lexing.MistLexer().Lex(text, diagnostics), diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(result.Root);

        return result.Root;
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