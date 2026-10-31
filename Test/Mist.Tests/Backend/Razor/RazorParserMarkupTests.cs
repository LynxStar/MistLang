using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Source;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class RazorParserMarkupTests
{
    [Fact]
    public void AnElementHoldsItsTextAndTheSpansOfBothTags()
    {
        var element = RazorParse.Single<MarkupElement>("<p>hello</p>");

        Assert.Equal("p", element.Name);
        Assert.Equal(new SourceSpan(1, 1), element.NameSpan);
        Assert.Equal(new SourceSpan(0, 3), element.StartTag);
        Assert.Equal(new SourceSpan(8, 4), element.EndTag);
        Assert.Equal(new SourceSpan(0, 12), element.Span);
        Assert.Equal("hello", Assert.IsType<MarkupText>(Assert.Single(element.Children)).Text);
    }

    [Fact]
    public void ElementsNestInSourceOrder()
    {
        var outer = RazorParse.Single<MarkupElement>("<div><span>a</span><b>c</b></div>");

        Assert.Equal(["span", "b"], outer.Children.Cast<MarkupElement>().Select(child => child.Name));
    }

    [Fact]
    public void VoidAndSelfClosingElementsTakeNoEndTag()
    {
        var nodes = RazorParse.Content("<br><input type=\"text\"><Routes />");

        var elements = nodes.Cast<MarkupElement>().ToArray();
        Assert.Equal(["br", "input", "Routes"], elements.Select(element => element.Name));
        Assert.All(elements, element => Assert.Empty(element.Children));
        Assert.All(elements, element => Assert.Null(element.EndTag));
        Assert.Equal([false, false, true], elements.Select(element => element.SelfClosing));
    }

    [Fact]
    public void AttributesAreQuotedSingleQuotedUnquotedOrValueless()
    {
        var element = RazorParse.Single<MarkupElement>("<a href=\"/x\" title='t' data-n=5 hidden>a</a>");

        var attributes = element.Attributes.Cast<HtmlAttribute>().ToArray();
        Assert.Equal(["href", "title", "data-n", "hidden"], attributes.Select(attribute => attribute.Name));
        Assert.Equal(["/x", "t", "5"], attributes[..3].Select(attribute => Assert.IsType<MarkupText>(Assert.Single(attribute.Value!.Parts)).Text));
        Assert.Null(attributes[3].Value);
        Assert.Equal(new SourceSpan(3, 9), attributes[0].Span);
        Assert.Equal(new SourceSpan(9, 2), attributes[0].Value!.Span);
    }

    [Fact]
    public void AnAttributeValueThatIsOneCodeRegionHoldsOnlyTheExpression()
    {
        var implicitValue = RazorParse.Single<MarkupElement>("<p class=\"@cls\">a</p>");
        var explicitValue = RazorParse.Single<MarkupElement>("<p class=\"@(a + b)\">a</p>");

        var implicitPart = Assert.IsType<ImplicitExpression>(Assert.Single(Value(implicitValue).Parts));
        Assert.Equal("cls", implicitPart.Code.Text);
        Assert.Equal(new SourceSpan(11, 3), implicitPart.Code.Span);
        Assert.Equal("a + b", Assert.IsType<ExplicitExpression>(Assert.Single(Value(explicitValue).Parts)).Code.Text);
    }

    [Fact]
    public void AnAttributeValueMixesLiteralTextAndCodeRegions()
    {
        var element = RazorParse.Single<MarkupElement>("<p class=\"btn @kind-x @(size) end\">a</p>");

        var parts = Value(element).Parts;
        Assert.Equal(5, parts.Length);
        Assert.Equal("btn ", Assert.IsType<MarkupText>(parts[0]).Text);
        Assert.Equal("kind", Assert.IsType<ImplicitExpression>(parts[1]).Code.Text);
        Assert.Equal("-x ", Assert.IsType<MarkupText>(parts[2]).Text);
        Assert.Equal("size", Assert.IsType<ExplicitExpression>(parts[3]).Code.Text);
        Assert.Equal(" end", Assert.IsType<MarkupText>(parts[4]).Text);
    }

    [Fact]
    public void AnAtBetweenLettersIsAnEmailAddressNotATransition()
    {
        var element = RazorParse.Single<MarkupElement>("<a href=\"mailto:me@example.com\">me@example.com</a>");

        Assert.Equal("mailto:me@example.com", Assert.IsType<MarkupText>(Assert.Single(Value(element).Parts)).Text);
        Assert.Equal("me@example.com", Assert.IsType<MarkupText>(Assert.Single(element.Children)).Text);
    }

    [Fact]
    public void ADoubledAtIsALiteralAtInTextAndAttributes()
    {
        var element = RazorParse.Single<MarkupElement>("<p title=\"@@x\">@@home and @@@name</p>");

        Assert.Equal("@x", Assert.IsType<MarkupText>(Assert.Single(Value(element).Parts)).Text);
        Assert.Equal(2, element.Children.Length);
        var text = Assert.IsType<MarkupText>(element.Children[0]);
        Assert.Equal("@home and @", text.Text);
        Assert.Equal(new SourceSpan(15, 13), text.Span);
        Assert.Equal("name", Assert.IsType<ImplicitExpression>(element.Children[1]).Code.Text);
    }

    [Fact]
    public void AnHtmlCommentIsMarkupAndHoldsNoTransition()
    {
        var comment = RazorParse.Single<HtmlComment>("<!-- note @x <p> -->");

        Assert.Equal("<!-- note @x <p> -->", comment.Text);
    }

    [Fact]
    public void ARazorCommentProducesItsOwnNodeAndHidesItsContent()
    {
        var nodes = RazorParse.Content("a@* hidden <p> @x *@b");

        Assert.Equal(3, nodes.Length);
        Assert.Equal("a", Assert.IsType<MarkupText>(nodes[0]).Text);
        Assert.Equal(new SourceSpan(1, 19), Assert.IsType<RazorComment>(nodes[1]).Span);
        Assert.Equal("b", Assert.IsType<MarkupText>(nodes[2]).Text);
    }

    [Fact]
    public void ADoctypeIsLiteralMarkup()
    {
        var nodes = RazorParse.Content("<!DOCTYPE html>\n<html></html>");

        Assert.Equal("<!DOCTYPE html>\n", Assert.IsType<MarkupText>(nodes[0]).Text);
        Assert.Equal("html", Assert.IsType<MarkupElement>(nodes[1]).Name);
    }

    [Fact]
    public void AScriptBodyIsTextWithTransitions()
    {
        var script = RazorParse.Single<MarkupElement>("<script>if (a < b) { x(@n); }</script>");

        Assert.Equal(3, script.Children.Length);
        Assert.Equal("if (a < b) { x(", Assert.IsType<MarkupText>(script.Children[0]).Text);
        Assert.Equal("n", Assert.IsType<ImplicitExpression>(script.Children[1]).Code.Text);
        Assert.Equal("); }", Assert.IsType<MarkupText>(script.Children[2]).Text);
    }

    [Fact]
    public void DirectiveAttributesSplitTheirParameterAndHoldTheirValueAsCode()
    {
        var element = RazorParse.Single<MarkupElement>(
            "<button @onclick=\"Increment\" @onclick:preventDefault @bind-Value=\"name\" @bind-Value:event=\"oninput\" "
            + "@key=\"item.Id\" @ref=\"button\" @attributes=\"extra\" @rendermode=\"InteractiveServer\">a</button>");

        var attributes = element.Attributes.Cast<DirectiveAttribute>().ToArray();
        Assert.Equal(
            [("onclick", null, "Increment"), ("onclick", "preventDefault", null), ("bind-Value", null, "name"), ("bind-Value", "event", "oninput"),
                ("key", null, "item.Id"), ("ref", null, "button"), ("attributes", null, "extra"), ("rendermode", null, "InteractiveServer")],
            attributes.Select(attribute => (attribute.Name, attribute.Parameter, attribute.Value?.Text)));
        Assert.Equal(new SourceSpan(8, 8), attributes[0].NameSpan);
        Assert.Equal(new SourceSpan(18, 9), attributes[0].Value!.Span);
    }

    [Theory]
    [InlineData("<button @onclick=\"@Increment\">a</button>", "Increment")]
    [InlineData("<button @onclick=\"@(() => count = count + 1)\">a</button>", "() => count = count + 1")]
    [InlineData("<button @onclick='() => Say(\"hi\")'>a</button>", "() => Say(\"hi\")")]
    public void ADirectiveAttributeValueMayRepeatTheTransition(string markup, string code)
    {
        var element = RazorParse.Single<MarkupElement>(markup);

        var value = Assert.IsType<DirectiveAttribute>(Assert.Single(element.Attributes)).Value!;
        Assert.Equal(code, value.Text);
        Assert.Equal(code, markup.Substring(value.Span.Start, value.Span.Length));
    }

    [Fact]
    public void AComponentTagIsAnElementWithItsAttributesAndChildContent()
    {
        var component = RazorParse.Single<MarkupElement>("<Greeting Title=\"Counter says\" Count=\"count\"><em>Nested</em></Greeting>");

        Assert.Equal("Greeting", component.Name);
        Assert.Equal(
            [("Title", "Counter says"), ("Count", "count")],
            component.Attributes.Cast<HtmlAttribute>().Select(attribute => (attribute.Name, Assert.IsType<MarkupText>(Assert.Single(attribute.Value!.Parts)).Text)));
        Assert.Equal("em", Assert.IsType<MarkupElement>(Assert.Single(component.Children)).Name);
    }

    [Fact]
    public void TemplatesAreChildTagsWithTheirContextAttribute()
    {
        var router = RazorParse.Single<MarkupElement>(
            "<Router AppAssembly=\"typeof(App).Assembly\">\n"
            + "    <Found Context=\"routeData\"><RouteView RouteData=\"@routeData\" /></Found>\n"
            + "    <NotFound><p>Sorry</p></NotFound>\n"
            + "</Router>");

        var templates = RazorParse.Significant(router.Children).Cast<MarkupElement>().ToArray();
        Assert.Equal(["Found", "NotFound"], templates.Select(template => template.Name));
        Assert.Equal("routeData", Assert.IsType<MarkupText>(Assert.Single(Assert.IsType<HtmlAttribute>(Assert.Single(templates[0].Attributes)).Value!.Parts)).Text);
        var view = Assert.IsType<MarkupElement>(Assert.Single(templates[0].Children));
        Assert.True(view.SelfClosing);
        Assert.Equal("routeData", Assert.IsType<ImplicitExpression>(Assert.Single(Assert.IsType<HtmlAttribute>(Assert.Single(view.Attributes)).Value!.Parts)).Code.Text);
    }

    [Fact]
    public void CarriageReturnLineEndingsParseLikeLineFeeds()
    {
        var element = RazorParse.Single<MarkupElement>("<p>\r\n    @count\r\n</p>");

        Assert.Equal("count", Assert.IsType<ImplicitExpression>(Assert.Single(RazorParse.Significant(element.Children))).Code.Text);
    }

    private static AttributeValue Value(MarkupElement element)
    {
        return Assert.IsType<HtmlAttribute>(Assert.Single(element.Attributes)).Value!;
    }
}