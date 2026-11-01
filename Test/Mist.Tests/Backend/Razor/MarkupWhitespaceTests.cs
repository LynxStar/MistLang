using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;

namespace Mist.Tests.Backend.Razor;

/// <summary>
/// Each case is a shape whose whitespace was read from the Razor compiler's generated C# for the
/// fixture and for probes beside it; <c>design.md</c>, Detailed Design 6, records the rules.
/// </summary>
[Trait("Tier", "1")]
public sealed class MarkupWhitespaceTests
{
    [Fact]
    public void TheFixtureCounterKeepsWhitespaceBetweenRootSiblingsAndDropsItAtTheEndsAndBesideCode()
    {
        var content = Root(
            H.Text("\n\n"), H.El("PageTitle", H.Text("Counter")), H.Text("\n\n"), H.El("h1", H.Text("Counter")), H.Text("\n\n"),
            H.El("p", H.Expr("count")), H.Text("\n"), H.El("p", H.Expr("int.Name")), H.Text("\n\n"),
            H.If("(lastReset is void)", H.El("p", H.Text("Never reset."))), H.Text("\n\n"),
            H.El("button", H.Text("Click me")), H.Text("\n"), H.El("button", H.Text("Reset")), H.Text("\n\n"),
            H.El("Greeting", H.Text("\n    "), H.El("em", H.Text("Nested")), H.Text("\n")), H.Text("\n\n"));

        var kept = MarkupWhitespace.Content(content);

        Assert.Equal(
            ["PageTitle", "\n\n", "h1", "\n\n", "p", "\n", "p", "if", "button", "\n", "button", "\n\n", "Greeting"],
            kept.Select(Describe));
    }

    [Fact]
    public void TheFixtureGreetingDropsTheIndentationAtTheEndsOfAnElementAndBesideCode()
    {
        var section = Root(H.El("section",
            H.Text("\n    "), H.El("h2", H.Expr("Title")), H.Text("\n    "), H.If("(ChildContent is RenderFragment body)", H.El("div", H.Expr("body"))),
            H.Text("\n    "), H.El("p", H.Expr("Count")), H.Text("\n")));

        var kept = MarkupWhitespace.Content(Assert.IsType<MarkupElement>(Assert.Single(section)).Children);

        Assert.Equal(["h2", "if", "p"], kept.Select(Describe));
    }

    [Fact]
    public void TextHoldingMoreThanWhitespaceIsKeptWholeAtTheEnds()
    {
        var kept = MarkupWhitespace.Content(Root(H.Text("\n    Lead text "), H.Expr("x"), H.Text("\n    trail text  \n")));

        Assert.Equal(["\n    Lead text ", "x", "\n    trail text  \n"], kept.Select(Describe));
    }

    [Fact]
    public void TheLineBreakEndingACodeBlocksLineIsSwallowed()
    {
        var kept = MarkupWhitespace.Content(Root(H.El("p", H.Expr("x")), H.If("(x)", H.El("b", H.Text("y"))), H.Text("  \na &amp; b\n"), H.If("(y)", H.Code("y = 1;")), H.Text("tail")));

        Assert.Equal(["p", "if", "a &amp; b\n", "if", "tail"], kept.Select(Describe));
    }

    [Fact]
    public void ARazorCommentKeepsTheTextsAroundItApartAndVanishesElsewhere()
    {
        var kept = MarkupWhitespace.Content(Root(
            H.RazorComment("lead"), H.Text("a "), H.RazorComment("gone"), H.RazorComment("again"), H.Text(" b"),
            H.RazorComment("before"), H.El("p", H.Expr("x")), H.RazorComment("tail")));

        Assert.Equal(["a ", "RazorComment", " b", "p"], kept.Select(Describe));
    }

    [Fact]
    public void AnHtmlCommentIsTransparentToTrimmingButKeepsTheTextBesideItApart()
    {
        var kept = MarkupWhitespace.Content(Root(
            H.Comment(" a "), H.Text("\n"), H.El("p", H.Expr("x")), H.Text(" "), H.Comment(" b "), H.Text(" c"),
            H.If("(x)", H.Code("x = 1;")), H.Text("\n"), H.Comment(" d "), H.Text("\ntail"), H.Text(" "), H.Comment(" e ")));

        Assert.Equal(["HtmlComment", "p", " ", "HtmlComment", " c", "if", "HtmlComment", "\ntail ", "HtmlComment"], kept.Select(Describe));
    }

    [Fact]
    public void BetweenMarkupLinesOfABlockTheLineBreakAndIndentationAreMarkup()
    {
        var block = Block(H.Code("var y = 1;"), H.El("p", H.Text("a")), H.El("p", H.Text("b")), H.Code("y = 2;"));

        var kept = MarkupWhitespace.CodeBody(block.Body, block.Source);

        Assert.Equal(["var y = 1;", "p", "\n    ", "p", "y = 2;"], kept.Select(Describe));
    }

    [Fact]
    public void ALineTransitionKeepsItsLineBreakAndATextTagStaysApartFromTheTextBesideIt()
    {
        var block = Block(H.Line(H.Text(" one")), H.Line(H.Text(" two")), H.TextTag(H.Text("t1")), H.TextTag(H.Text("t2")));

        var kept = MarkupWhitespace.CodeBody(block.Body, block.Source);

        Assert.Equal([" one\n     two\n", "t1", "t2"], kept.Select(Describe));
    }

    [Theory]
    [InlineData("text", "\n")]
    [InlineData("blank", "\n")]
    [InlineData("element", "\n")]
    [InlineData("expression", "\n")]
    [InlineData("comment", "\n")]
    [InlineData("text", "\r\n")]
    [InlineData("blank", "\r\n")]
    [InlineData("element", "\r\n")]
    [InlineData("expression", "\r\n")]
    [InlineData("comment", "\r\n")]
    public void ATrailingMemberBlockKeepsOnlyTheMarkupSideOfItsGap(string shape, string newline)
    {
        Hand[] markup = shape switch
        {
            "element" => [H.El("p", H.Text("a")), H.Text(newline)],
            "expression" => [H.Expr("x"), H.Text(newline)],
            "comment" => [H.El("span", H.Text("x")), H.Text(" y"), H.RazorComment("gone"), H.Text("z" + newline)],
            _ => [H.El("span", H.Text("x")), H.Text(" y" + newline + (shape == "blank" ? newline : ""))],
        };
        var tree = new HandTree().Content(markup).Code("", leading: "", trailing: "").Content(H.Text(newline)).Tree();

        var kept = MarkupWhitespace.Content(tree.Content, tree.CodeBlocks);

        string[] expected = shape switch
        {
            "element" => ["p"],
            "expression" => ["x"],
            "comment" => ["span", " y", "RazorComment", "z" + newline],
            _ => ["span", " y" + newline + (shape == "blank" ? newline : "")],
        };
        Assert.Equal(expected, kept.Select(Describe));
    }

    private static ImmutableArray<RazorNode> Root(params Hand[] nodes)
    {
        return new HandTree().Content(nodes).Tree().Content;
    }

    private static (ImmutableArray<RazorNode> Body, string Source) Block(params Hand[] body)
    {
        var tree = new HandTree().Content(H.Block(body)).Tree();
        return (Assert.IsType<StatementBlock>(Assert.Single(tree.Content)).Body, tree.Text.Contents);
    }

    private static string Describe(RazorNode node)
    {
        return node switch
        {
            MarkupText text => text.Text,
            MarkupElement element => element.Name,
            ControlBlock => "if",
            ImplicitExpression expression => expression.Code.Text,
            CodeRegion region => region.Text,
            _ => node.GetType().Name,
        };
    }
}