using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Source;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class RazorParserDirectiveTests
{
    [Theory]
    [InlineData("@page \"/counter\"", "page", "\"/counter\"")]
    [InlineData("@layout MainLayout", "layout", "MainLayout")]
    [InlineData("@inherits LayoutComponentBase", "inherits", "LayoutComponentBase")]
    [InlineData("@implements IDisposable", "implements", "IDisposable")]
    [InlineData("@inject NavigationManager Nav", "inject", "NavigationManager Nav")]
    [InlineData("@using Microsoft.AspNetCore.Components.Web", "using", "Microsoft.AspNetCore.Components.Web")]
    [InlineData("@namespace App.Pages", "namespace", "App.Pages")]
    [InlineData("@attribute [Authorize]", "attribute", "[Authorize]")]
    [InlineData("@rendermode InteractiveServer", "rendermode", "InteractiveServer")]
    [InlineData("@typeparam TItem", "typeparam", "TItem")]
    [InlineData("@preservewhitespace true", "preservewhitespace", "true")]
    public void EachComponentDirectiveTakesTheRestOfItsLineAsItsArgument(string line, string name, string argument)
    {
        var tree = RazorParse.Clean(line + "\n<p>a</p>");

        var directive = Assert.Single(tree.Directives);
        Assert.Equal(name, directive.Name);
        Assert.Equal(new SourceSpan(0, name.Length + 1), directive.KeywordSpan);
        Assert.Equal(argument, directive.Argument!.Text);
        Assert.Equal(new SourceSpan(line.Length - argument.Length, argument.Length), directive.Argument.Span);
        Assert.Equal(new SourceSpan(0, line.Length), directive.Span);
        Assert.Equal("p", Assert.IsType<MarkupElement>(Assert.Single(RazorParse.Significant(tree.Content))).Name);
    }

    [Fact]
    public void ADirectiveArgumentStopsBeforeACarriageReturn()
    {
        var tree = RazorParse.Clean("  @using App.Models\r\n@page \"/x\"\r\n");

        Assert.Equal(["App.Models", "\"/x\""], tree.Directives.Select(directive => directive.Argument!.Text));
    }

    [Fact]
    public void ACodeBlockHoldsItsMembersAndTwoBlocksStayInOrder()
    {
        var tree = RazorParse.Clean("<p>a</p>\n@code {\n    private int count = 0;\n}\n@code\n{\n    void Go() { count = 1; }\n}");

        Assert.Equal(["private int count = 0;", "void Go() { count = 1; }"], tree.CodeBlocks.Select(block => block.Body.Text));
        Assert.Equal(9, tree.CodeBlocks[0].Span.Start);
        Assert.Empty(tree.Directives);
        Assert.Equal("p", Assert.IsType<MarkupElement>(Assert.Single(RazorParse.Significant(tree.Content))).Name);
    }

    [Fact]
    public void ACodeBlockBalancesOverMistLiterals()
    {
        var tree = RazorParse.Clean("@code {\n    string Close = \"}\";\n    char Brace = '}';\n}");

        Assert.Equal("string Close = \"}\";\n    char Brace = '}';", Assert.Single(tree.CodeBlocks).Body.Text);
    }

    [Theory]
    [InlineData("<p>@page</p>")]
    [InlineData("<p>\n@using</p>")]
    public void ADirectiveNameAwayFromTheTopLevelIsAnImplicitExpression(string markup)
    {
        var tree = RazorParse.Clean(markup);

        Assert.Empty(tree.Directives);
        _ = Assert.IsType<ImplicitExpression>(Assert.Single(RazorParse.Significant(Assert.IsType<MarkupElement>(Assert.Single(tree.Content)).Children)));
    }

    [Theory]
    [InlineData("@count")]
    [InlineData("@count.Value")]
    [InlineData("@count<br>")]
    [InlineData("@Title shown here")]
    public void ALineStartingWithAnExpressionIsNotADirective(string markup)
    {
        var tree = RazorParse.Clean(markup);

        Assert.Empty(tree.Directives);
        _ = Assert.IsType<ImplicitExpression>(tree.Content[0]);
    }

    [Theory]
    [InlineData("Counter.razor")]
    [InlineData("Greeting.razor")]
    [InlineData("_Imports.razor")]
    public void EveryFixtureComponentParsesCleanlyAndItsCodeRegionsAreVerbatim(string fileName)
    {
        var contents = RazorParse.Fixture(fileName);

        var tree = RazorParse.Clean(contents);

        Assert.All(RazorParse.CodeRegions(tree), region => Assert.Equal(region.Text, contents.Substring(region.Span.Start, region.Span.Length)));
    }

    [Fact]
    public void TheFixtureCounterHasItsRouteMarkupConditionalButtonsComponentAndCode()
    {
        var tree = RazorParse.Clean(RazorParse.Fixture("Counter.razor"));

        Assert.Equal(("page", "\"/counter\""), (Assert.Single(tree.Directives).Name, tree.Directives[0].Argument!.Text));
        var content = RazorParse.Significant(tree.Content);
        Assert.Equal(
            ["PageTitle", "h1", "p", "p", "if", "button", "button", "Greeting"],
            content.Select(node => node is MarkupElement element ? element.Name : Assert.IsType<ControlBlock>(node).Arms[0].Keyword));
        Assert.Equal("count", Assert.IsType<ImplicitExpression>(((MarkupElement)content[2]).Children[1]).Code.Text);
        Assert.Equal("int.Name", Assert.IsType<ExplicitExpression>(((MarkupElement)content[3]).Children[1]).Code.Text);
        var conditional = (ControlBlock)content[4];
        Assert.Equal(["if", "else"], conditional.Arms.Select(arm => arm.Keyword));
        Assert.Equal("(lastReset is void)", conditional.Arms[0].Header!.Text);
        var reset = Assert.IsType<MarkupElement>(Assert.Single(conditional.Arms[1].Body));
        Assert.Equal("lastReset", Assert.IsType<ImplicitExpression>(reset.Children[1]).Code.Text);
        Assert.Equal(".", Assert.IsType<MarkupText>(reset.Children[2]).Text);
        var click = Assert.IsType<DirectiveAttribute>(((MarkupElement)content[5]).Attributes[1]);
        Assert.Equal(("onclick", "Increment"), (click.Name, click.Value!.Text));
        Assert.Equal(["Title", "Count"], ((MarkupElement)content[7]).Attributes.Select(attribute => attribute.Name));
        var code = Assert.Single(tree.CodeBlocks).Body.Text;
        Assert.StartsWith("private int count = 0;", code, StringComparison.Ordinal);
        Assert.EndsWith("count = 0;\n    }", code.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFixtureGreetingNestsAConditionalInsideItsSection()
    {
        var tree = RazorParse.Clean(RazorParse.Fixture("Greeting.razor"));

        var section = Assert.IsType<MarkupElement>(Assert.Single(RazorParse.Significant(tree.Content)));
        var children = RazorParse.Significant(section.Children);
        Assert.Equal("h2", Assert.IsType<MarkupElement>(children[0]).Name);
        var conditional = Assert.IsType<ControlBlock>(children[1]);
        Assert.Equal("(ChildContent is RenderFragment body)", Assert.Single(conditional.Arms).Header!.Text);
        var body = Assert.IsType<MarkupElement>(Assert.Single(conditional.Arms[0].Body));
        Assert.Equal("body", Assert.IsType<ImplicitExpression>(Assert.Single(body.Children)).Code.Text);
        Assert.Equal("Count", Assert.IsType<ImplicitExpression>(Assert.IsType<MarkupElement>(children[2]).Children[1]).Code.Text);
        Assert.Contains("public RenderFragment? ChildContent { get; set; }", Assert.Single(tree.CodeBlocks).Body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFixtureImportsFileIsTwoUsingDirectives()
    {
        var tree = RazorParse.Clean(RazorParse.Fixture("_Imports.razor"));

        Assert.Equal(
            [("using", "Microsoft.AspNetCore.Components.Web"), ("using", "RazorComponents.Components")],
            tree.Directives.Select(directive => (directive.Name, directive.Argument!.Text)));
        Assert.Empty(RazorParse.Significant(tree.Content));
    }
}