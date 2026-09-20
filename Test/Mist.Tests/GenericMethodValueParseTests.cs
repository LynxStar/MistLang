// Tier 1 (Unit): the parser alone. A generic method named as a value with its type arguments stated,
// `Api.Same<int>` with no call after it, is one suffix or one primary exactly where `>` is followed by a
// token that ends an expression; a comparison chain, a call and a declaration keep the parse they had.

using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Tests;

public sealed class GenericMethodValueParseTests
{
    [Theory]
    [InlineData("func<int, int> f = Api.Same<int>;")]
    [InlineData("var r = Use(Api.Same<int>);")]
    [InlineData("var r = Use(Api.Same<int>, 3);")]
    [InlineData("var r = Use(3, Api.Same<int>);")]
    [InlineData("return Api.Same<int>;")]
    [InlineData("held = Api.Same<int>;")]
    [InlineData("var all = [Api.Same<int>, other];")]
    [InlineData("var all = [other, Api.Same<int>];")]
    [InlineData("var pair = (Api.Same<int>, 1);")]
    [InlineData("var made = () => Api.Same<int>;")]
    [InlineData("var made = new Holder { Held = Api.Same<int> };")]
    [InlineData("var deep = Api.Same<List<List<int>>>;")]
    [InlineData("var shown = box.inner.Show<int, string>;")]
    public void AStatedMethodValueIsOneSuffixWhereverAnExpressionEnds(string statement)
    {
        var suffix = Assert.Single(Descendants(Parse(statement)).OfType<Generated.GenericMethodGroupSuffixPostfixSuffixNode>());

        Assert.Contains(suffix.Name.Lexeme, new[] { "Same", "Show" });
    }

    [Theory]
    [InlineData("func<int, int> f = Same<int>;")]
    [InlineData("var r = Use(Same<int>, 3);")]
    [InlineData("return Same<int>;")]
    public void AnUnqualifiedStatedMethodValueIsOnePrimary(string statement)
    {
        var primary = Assert.Single(Descendants(Parse(statement)).OfType<Generated.GenericNameExpressionPrimaryNode>());

        Assert.Equal("Same", primary.Name.Lexeme);
    }

    [Theory]
    [InlineData("var r = Use(a < b, c > d);")]
    [InlineData("var r = Use(x.a < b, c > d);")]
    [InlineData("var r = a < b > -c;")]
    [InlineData("var r = a<b>=c;")]
    [InlineData("var r = Api.Same<int>(3);")]
    [InlineData("var r = Same<int>(3);")]
    [InlineData("var all = Task<int>.WhenAll(pending);")]
    [InlineData("Box<Span<T, unknown>> b = made;")]
    [InlineData("Box<Span<T, unknown>> b;")]
    [InlineData("var r = (Cell<int>)(3);")]
    [InlineData("var flags = [a < b, c > d];")]
    public void WhatWasAComparisonACallOrADeclarationStaysOne(string statement)
    {
        var nodes = Descendants(Parse(statement)).ToList();

        Assert.Empty(nodes.OfType<Generated.GenericMethodGroupSuffixPostfixSuffixNode>());
        Assert.Empty(nodes.OfType<Generated.GenericNameExpressionPrimaryNode>());
    }

    [Theory]
    [InlineData("var r = Use(a < b, c > d);", 2)]
    [InlineData("var flags = [a < b, c > d];", 2)]
    [InlineData("var r = Use(Api.Same<int>, a < b);", 1)]
    public void AComparisonBesideAMethodValueIsStillAComparison(string statement, int comparisons)
    {
        Assert.Equal(comparisons, Descendants(Parse(statement)).OfType<Generated.RelationalComparisonRelationalNode>().Count(node => node.Group0.Length > 0));
    }

    [Theory]
    [InlineData("func<int, int> f = Api.Same<int>;")]
    [InlineData("var r = Use(Same<int>, Api.Same<int>);")]
    public void PrintingAStatedMethodValueAndParsingItBackIsTheSameTree(string statement)
    {
        var first = Parse(statement);
        var printed = MistPrettyPrinter.Print(first);
        var diagnostics = new DiagnosticBag();
        var second = new MistParser().Parse(new MistLexer().Lex(new SourceText("printed.mist", printed), diagnostics), diagnostics).Root;

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.NotNull(second);
        Assert.Equal(Shape(first), Shape(second));
    }

    private static string Shape(Storm.Runtime.SyntaxNode node)
    {
        return $"{node.GetType().Name}({string.Join(",", node.Children().Where(child => child is not null).Select(Shape))})";
    }

    private static Storm.Runtime.SyntaxNode Parse(string statement)
    {
        var source = new SourceText(
            "probe.mist", $"public class Probe {{ public static func<int, int> Run() {{ {statement} }} }}");
        var diagnostics = new DiagnosticBag();
        var result = new MistParser().Parse(new MistLexer().Lex(source, diagnostics), diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
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