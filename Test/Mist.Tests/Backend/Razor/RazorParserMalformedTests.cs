using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class RazorParserMalformedTests
{
    [Theory]
    [InlineData("<p>a</p>\n<div>\n<p>a</p>", 9, 4)]
    [InlineData("<div class=\"a\"", 0, 4)]
    [InlineData("<div class=\"a>text", 0, 4)]
    [InlineData("<div\n<p>a</p>", 0, 4)]
    public void AnElementNeverClosedIsReportedAtItsStartTag(string markup, int offset, int length)
    {
        var error = RazorParse.Error(markup);

        Assert.Same(RazorDiagnostics.UnclosedElement, error.Descriptor);
        Assert.Equal((offset, length), RazorParse.Where(error));
        Assert.Equal("The element 'div' is opened here and never closed.", error.Message);
    }

    [Fact]
    public void AnEndTagClosingAnAncestorReportsTheUnclosedChildOnce()
    {
        var error = RazorParse.Error("<div><span>a</div>");

        Assert.Same(RazorDiagnostics.UnclosedElement, error.Descriptor);
        Assert.Equal((5, 5), RazorParse.Where(error));
    }

    [Fact]
    public void AnUnclosedHtmlCommentIsAnUnclosedElement()
    {
        var error = RazorParse.Error("<p>a</p><!-- trailing");

        Assert.Same(RazorDiagnostics.UnclosedElement, error.Descriptor);
        Assert.Equal((8, 4), RazorParse.Where(error));
    }

    [Fact]
    public void AnEndTagNamingAnotherElementIsMismatchedAtTheEndTag()
    {
        var error = RazorParse.Error("<div>\n  a</span>");

        Assert.Same(RazorDiagnostics.MismatchedEndTag, error.Descriptor);
        Assert.Equal((9, 7), RazorParse.Where(error));
        Assert.Equal((2, 4), RazorParse.LineAndColumn(error));
        Assert.Equal("The end tag '</span>' matches no open element; the innermost open element is '<div>'.", error.Message);
    }

    [Theory]
    [InlineData("a</p>", 1, 4)]
    [InlineData("@{ var a = 1; </p> }", 14, 4)]
    public void AnEndTagWithNothingOpenIsMismatched(string markup, int offset, int length)
    {
        var error = RazorParse.Error(markup);

        Assert.Same(RazorDiagnostics.MismatchedEndTag, error.Descriptor);
        Assert.Equal((offset, length), RazorParse.Where(error));
        Assert.Equal("The end tag '</p>' matches no open element.", error.Message);
    }

    [Theory]
    [InlineData("@(a + (b)", 0, 2, "explicit expression")]
    [InlineData("<p>a</p>\n@{ var a = 1;", 9, 2, "statement block")]
    [InlineData("@if (a) { <p>x</p>", 0, 3, "control block")]
    [InlineData("@if (a <p>x</p>", 0, 3, "control block")]
    [InlineData("@if (a) { } else <p>x</p>", 12, 4, "control block")]
    [InlineData("@foreach (var x in xs) { <p>x</p>", 0, 8, "control block")]
    [InlineData("@code {\n    int a;", 0, 5, "code block")]
    [InlineData("@code\n<p>a</p>", 0, 5, "code block")]
    [InlineData("@* never closed", 0, 2, "comment")]
    [InlineData("@Show(a, \")\"", 0, 6, "implicit expression")]
    public void ACodeRegionWithoutItsCloseIsUnterminatedAtTheTransition(string markup, int offset, int length, string region)
    {
        var error = RazorParse.Error(markup);

        Assert.Same(RazorDiagnostics.UnterminatedCodeRegion, error.Descriptor);
        Assert.Equal((offset, length), RazorParse.Where(error));
        Assert.Equal($"The {region} opened by this transition is never closed.", error.Message);
    }

    [Theory]
    [InlineData("@foo Bar", "foo", 0)]
    [InlineData("<p>a</p>\n  @pages \"/x\"", "pages", 11)]
    public void ALowercaseNameWithAnArgumentOnATopLevelLineIsAnUnknownDirective(string markup, string name, int offset)
    {
        var error = RazorParse.Error(markup);

        Assert.Same(RazorDiagnostics.UnknownDirective, error.Descriptor);
        Assert.Equal((offset, name.Length + 1), RazorParse.Where(error));
        Assert.Equal($"'@{name}' begins a directive line but names no directive a component accepts.", error.Message);
    }

    [Theory]
    [InlineData("model", "@model Person")]
    [InlineData("section", "@section Scripts {\n    <script>go()</script>\n}")]
    [InlineData("addTagHelper", "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers")]
    [InlineData("removeTagHelper", "@removeTagHelper *, Lib")]
    [InlineData("tagHelperPrefix", "@tagHelperPrefix th:")]
    [InlineData("helper", "@helper Show()\n{\n    <b>x</b>\n}")]
    public void AnMvcDirectiveIsNotValidInAComponentAndItsBlockIsSkipped(string name, string directive)
    {
        var (tree, diagnostics) = RazorParse.Text(directive + "\n<p>after</p>");

        var error = Assert.Single(diagnostics);
        Assert.Same(RazorDiagnostics.DirectiveNotValidInComponent, error.Descriptor);
        Assert.Equal((0, name.Length + 1), RazorParse.Where(error));
        Assert.Equal($"'@{name}' is a directive of MVC views and pages, which a component does not accept.", error.Message);
        Assert.Equal("p", Assert.IsType<MarkupElement>(Assert.Single(RazorParse.Significant(tree.Content))).Name);
    }

    [Theory]
    [InlineData("@functions {\n    int count;\n}")]
    [InlineData("@functions\n{\n    void Go() { }\n}")]
    public void TheFunctionsDirectiveIsNotSupportedAndDeclaresNothing(string directive)
    {
        var (tree, diagnostics) = RazorParse.Text(directive + "\n<p>after</p>");

        var error = Assert.Single(diagnostics);
        Assert.Same(RazorDiagnostics.FunctionsDirectiveNotSupported, error.Descriptor);
        Assert.Equal((0, 10), RazorParse.Where(error));
        Assert.Empty(tree.CodeBlocks);
        Assert.Equal("p", Assert.IsType<MarkupElement>(Assert.Single(RazorParse.Significant(tree.Content))).Name);
    }

    [Theory]
    [InlineData("<p>@ x</p>", 3)]
    [InlineData("<p>@1</p>", 3)]
    [InlineData("<p>@:x</p>", 3)]
    [InlineData("<p>a @</p>", 5)]
    [InlineData("<p class=\"@ x\">a</p>", 10)]
    public void AnAtThatStartsNoRegionIsAnInvalidTransition(string markup, int offset)
    {
        var error = RazorParse.Error(markup);

        Assert.Same(RazorDiagnostics.InvalidTransition, error.Descriptor);
        Assert.Equal((offset, 1), RazorParse.Where(error));
    }

    [Fact]
    public void EveryErrorInAFileIsReportedInSourceOrder()
    {
        var (tree, diagnostics) = RazorParse.Text(
            "@foo Bar\n"
            + "@model Person\n"
            + "@functions { }\n"
            + "<div>a</span>\n"
            + "<p>@ x</p>\n"
            + "@if (a) { <b>x</i> }\n"
            + "@{ var a = 1;");

        Assert.True(tree.HasErrors);
        Assert.Equal(
            [
                (RazorDiagnostics.UnknownDirective, (1, 1)),
                (RazorDiagnostics.DirectiveNotValidInComponent, (2, 1)),
                (RazorDiagnostics.FunctionsDirectiveNotSupported, (3, 1)),
                (RazorDiagnostics.MismatchedEndTag, (4, 7)),
                (RazorDiagnostics.InvalidTransition, (5, 4)),
                (RazorDiagnostics.MismatchedEndTag, (6, 15)),
                (RazorDiagnostics.UnterminatedCodeRegion, (7, 1)),
            ],
            diagnostics.Select(diagnostic => (diagnostic.Descriptor, RazorParse.LineAndColumn(diagnostic))));
    }
}