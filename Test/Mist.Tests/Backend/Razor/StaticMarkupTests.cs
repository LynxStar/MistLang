using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class StaticMarkupTests
{
    [Fact]
    public void AStaticElementTakesTheTextAfterItButNotTheTextBefore()
    {
        var (statics, nodes) = Statics(H.Text("\n\n"), H.El("h1", H.Text("Counter")), H.Text("\n\n"), H.El("p", H.Expr("count")));

        var runs = statics.Runs(nodes);

        Assert.Equal([("\n\n", true), ("<h1>Counter</h1>\n\n", true), ("p", false)], runs.Select(run => Describe(statics, run)));
    }

    [Fact]
    public void ARunGrowsLeftAcrossEmptyElementsAndTextUntilAnElementWithContent()
    {
        var (statics, nodes) = Statics(
            H.Text("  \n  "), H.El("b", H.Text("bold")), H.Text(" mid "), H.El("i", H.Text("it")), H.Text("\n"),
            H.Void("hr"), H.Text("\n"), H.Void("hr"), H.Text("\n"));

        var runs = statics.Runs(nodes);

        Assert.Equal([("  \n  ", true), ("<b>bold</b> mid ", true), ("<i>it</i>\n<hr>\n<hr>\n", true)], runs.Select(run => Describe(statics, run)));
    }

    [Fact]
    public void AnElementWithACodeRegionAComponentOrADirectiveAttributeIsNotStatic()
    {
        var (statics, nodes) = Statics(
            H.El("p", [H.Attr("title", H.Expr("t"))], H.Text("x")), H.El("Card", H.Text("x")), H.El("a", [H.Dir("onclick", "Go")], H.Text("x")),
            H.El("div", H.El("span", H.Expr("y"))));

        Assert.All(nodes, node => Assert.False(statics.IsStatic(node)));
    }

    [Fact]
    public void StaticHtmlIsRenderedAsTheRazorCompilerRendersIt()
    {
        var (statics, nodes) = Statics(H.El("div",
            [H.Attr("class", ""), H.Attr("id", "x", '\''), H.Bare("hidden")],
            H.Text(" "), H.Void("br"), H.El("span"), H.Void("custom-el"), H.El("p", [H.Attr("title", "a&amp;b")], H.Text("q &lt; r")), H.Text("\n")));

        Assert.Equal("<div class id='x' hidden><br><span></span><custom-el></custom-el><p title=\"a&amp;b\">q &lt; r</p></div>", statics.Render(nodes));
    }

    [Theory]
    [InlineData("Counter", "Counter")]
    [InlineData("Current count: ", "Current count: ")]
    [InlineData("a &amp; b ", "a & b ")]
    [InlineData("&lt;&#65;&#x42;", "<AB")]
    public void TextWithNoLineBreakTabOrUndecodableEntityIsPlainContent(string text, string decoded)
    {
        Assert.True(StaticMarkup.TryPlainText(text, out var result));
        Assert.Equal(decoded, result);
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData(" text\n")]
    [InlineData("tab\there")]
    [InlineData("a & b")]
    [InlineData("a &amp b")]
    [InlineData("a &bogus; b")]
    [InlineData("café")]
    public void TextWithALineBreakATabOrAnUndecodableEntityStaysMarkup(string text)
    {
        Assert.False(StaticMarkup.TryPlainText(text, out _));
    }

    private static (StaticMarkup Statics, ImmutableArray<RazorNode> Nodes) Statics(params Hand[] nodes)
    {
        var tree = new HandTree().Content(nodes).Tree();
        return (new StaticMarkup(tree.Text.Contents, element => char.IsUpper(element.Name[0])), tree.Content);
    }

    private static (string, bool) Describe(StaticMarkup statics, ContentRun run)
    {
        return (run.IsStatic ? statics.Render(run.Nodes) : ((MarkupElement)run.Nodes[0]).Name, run.IsStatic);
    }
}