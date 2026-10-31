using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Source;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class RazorParserCodeTests
{
    [Theory]
    [InlineData("@user.Name.Length", "user.Name.Length")]
    [InlineData("@items[i].Format(\"x\", y)", "items[i].Format(\"x\", y)")]
    [InlineData("@Show(\")\")", "Show(\")\")")]
    [InlineData("@Show(')')", "Show(')')")]
    [InlineData("@Show('\\'', ']')", "Show('\\'', ']')")]
    [InlineData("@Show($\"{Wrap(\")\")})\")", "Show($\"{Wrap(\")\")})\")")]
    [InlineData("@Show(`[(]`)", "Show(`[(]`)")]
    [InlineData("@Show(a // )\n)", "Show(a // )\n)")]
    [InlineData("@grid[rows[0]]", "grid[rows[0]]")]
    public void AnImplicitExpressionRunsThroughMembersCallsAndIndexersBalancedOverMistLiterals(string markup, string code)
    {
        var expression = RazorParse.Single<ImplicitExpression>(markup);

        Assert.Equal(code, expression.Code.Text);
        Assert.Equal(new SourceSpan(1, code.Length), expression.Code.Span);
        Assert.Equal(new SourceSpan(0, markup.Length), expression.Span);
    }

    [Theory]
    [InlineData("@count.", "count", ".")]
    [InlineData("@count. more", "count", ". more")]
    [InlineData("@name!", "name", "!")]
    [InlineData("@a.b(c) d", "a.b(c)", " d")]
    [InlineData("@total-1", "total", "-1")]
    public void AnImplicitExpressionEndsAtTheFirstCharacterItCannotContinueWith(string markup, string code, string rest)
    {
        var nodes = RazorParse.Content(markup);

        Assert.Equal(2, nodes.Length);
        Assert.Equal(code, Assert.IsType<ImplicitExpression>(nodes[0]).Code.Text);
        Assert.Equal(rest, Assert.IsType<MarkupText>(nodes[1]).Text);
    }

    [Theory]
    [InlineData("@(int.Name)", "int.Name")]
    [InlineData("@(Format(\"(\") + (1))", "Format(\"(\") + (1)")]
    [InlineData("@( spaced )", "spaced")]
    [InlineData("@(a\n  + b)", "a\n  + b")]
    public void AnExplicitExpressionHoldsTheBalancedParenthesizedText(string markup, string code)
    {
        var expression = RazorParse.Single<ExplicitExpression>(markup);

        Assert.Equal(code, expression.Code.Text);
        Assert.Equal(code, markup.Substring(expression.Code.Span.Start, expression.Code.Span.Length));
    }

    [Fact]
    public void AStatementBlockHoldsMistStatements()
    {
        var block = RazorParse.Single<StatementBlock>("@{ var x = 1; }");

        var code = Assert.IsType<CodeRegion>(Assert.Single(block.Body));
        Assert.Equal("var x = 1;", code.Text);
        Assert.Equal(new SourceSpan(3, 10), code.Span);
    }

    [Fact]
    public void AnElementAtTheStartOfACodeLineSwitchesToMarkupUntilItCloses()
    {
        var block = RazorParse.Single<StatementBlock>("@{\n    var n = 2;\n    <p>Count @n</p>\n    n = 3;\n}");

        Assert.Equal(3, block.Body.Length);
        Assert.Equal("var n = 2;", Assert.IsType<CodeRegion>(block.Body[0]).Text);
        var element = Assert.IsType<MarkupElement>(block.Body[1]);
        Assert.Equal("n", Assert.IsType<ImplicitExpression>(element.Children[1]).Code.Text);
        Assert.Equal("n = 3;", Assert.IsType<CodeRegion>(block.Body[2]).Text);
    }

    [Theory]
    [InlineData("@{ var ok = a < b; }", "var ok = a < b;")]
    [InlineData("@{ var s = \"}\"; }", "var s = \"}\";")]
    [InlineData("@{ var c = '}'; }", "var c = '}';")]
    [InlineData("@{ var r = `}<p>`; }", "var r = `}<p>`;")]
    [InlineData("@{ var i = $\"{x}}\"; }", "var i = $\"{x}}\";")]
    [InlineData("@{ Log(); // } <p>\n}", "Log(); // } <p>")]
    public void AComparisonOrABraceInsideALiteralOrCommentIsMistText(string markup, string code)
    {
        var block = RazorParse.Single<StatementBlock>(markup);

        Assert.Equal(code, Assert.IsType<CodeRegion>(Assert.Single(block.Body)).Text);
    }

    [Fact]
    public void MarkupInsideANestedMistBlockSplitsTheCodeAroundIt()
    {
        var block = RazorParse.Single<StatementBlock>("@{ if (x) { <b>y</b> } }");

        Assert.Equal(3, block.Body.Length);
        Assert.Equal("if (x) {", Assert.IsType<CodeRegion>(block.Body[0]).Text);
        Assert.Equal("b", Assert.IsType<MarkupElement>(block.Body[1]).Name);
        Assert.Equal("}", Assert.IsType<CodeRegion>(block.Body[2]).Text);
    }

    [Theory]
    [InlineData("@if (a) { <p>x</p> }", "if", "(a)")]
    [InlineData("@while (go) { <p>x</p> }", "while", "(go)")]
    [InlineData("@for (var i = 0; i < n; i++) { <p>x</p> }", "for", "(var i = 0; i < n; i++)")]
    [InlineData("@foreach (var item in items) { <p>x</p> }", "foreach", "(var item in items)")]
    [InlineData("@readonly { <p>x</p> }", "readonly", null)]
    [InlineData("@with (Scope()) { <p>x</p> }", "with", "(Scope())")]
    [InlineData("@with (var s = Scope()) { <p>x</p> }", "with", "(var s = Scope())")]
    public void EachControlBlockFormHoldsItsHeaderAndAMarkupBody(string markup, string keyword, string? header)
    {
        var block = RazorParse.Single<ControlBlock>(markup);

        var arm = Assert.Single(block.Arms);
        Assert.Equal(keyword, arm.Keyword);
        Assert.Equal(header, arm.Header?.Text);
        Assert.Equal("p", Assert.IsType<MarkupElement>(Assert.Single(arm.Body)).Name);
        Assert.Equal(new SourceSpan(0, markup.Length), block.Span);
    }

    [Fact]
    public void ElseIfAndElseArmsContinueAnIfBlockAcrossLines()
    {
        var block = RazorParse.Single<ControlBlock>("@if (a)\n{\n    <p>a</p>\n}\nelse if (b)\n{\n}\nelse\n{\n    <p>c</p>\n}");

        Assert.Equal(["if", "else if", "else"], block.Arms.Select(arm => arm.Keyword));
        Assert.Equal(["(a)", "(b)", null], block.Arms.Select(arm => arm.Header?.Text));
        Assert.Equal([1, 0, 1], block.Arms.Select(arm => arm.Body.Length));
    }

    [Fact]
    public void AnIfWithoutElseLeavesTheFollowingMarkupAlone()
    {
        var nodes = RazorParse.Content("@if (a) { <p>x</p> }\nelsewhere");

        _ = Assert.Single(Assert.IsType<ControlBlock>(nodes[0]).Arms);
        Assert.Equal("\nelsewhere", Assert.IsType<MarkupText>(nodes[1]).Text);
    }

    [Fact]
    public void AKeywordPrefixIsAnImplicitExpression()
    {
        Assert.Equal("iffy", RazorParse.Single<ImplicitExpression>("@iffy").Code.Text);
    }

    [Fact]
    public void ATextLineIsMarkupToTheEndOfItsLine()
    {
        var block = RazorParse.Single<ControlBlock>("@if (a) {\n    @:Hello @name!\n    Next();\n}");

        var body = Assert.Single(block.Arms).Body;
        var line = Assert.IsType<TextLine>(body[0]);
        Assert.Equal(3, line.Content.Length);
        Assert.Equal("Hello ", Assert.IsType<MarkupText>(line.Content[0]).Text);
        Assert.Equal("name", Assert.IsType<ImplicitExpression>(line.Content[1]).Code.Text);
        Assert.Equal("!", Assert.IsType<MarkupText>(line.Content[2]).Text);
        Assert.Equal("Next();", Assert.IsType<CodeRegion>(body[1]).Text);
    }

    [Fact]
    public void ATextTagInCodeIsMarkupWithTheTagsDropped()
    {
        var block = RazorParse.Single<ControlBlock>("@if (a) { <text>Hi @name</text> }");

        var tag = Assert.IsType<TextTag>(Assert.Single(Assert.Single(block.Arms).Body));
        Assert.Equal(2, tag.Children.Length);
        Assert.Equal("Hi ", Assert.IsType<MarkupText>(tag.Children[0]).Text);
        Assert.Equal("name", Assert.IsType<ImplicitExpression>(tag.Children[1]).Code.Text);
    }

    [Fact]
    public void ATextTagInMarkupIsAnOrdinaryElement()
    {
        Assert.Equal("text", RazorParse.Single<MarkupElement>("<text>a</text>").Name);
    }

    [Fact]
    public void ARazorCommentInCodeIsItsOwnNode()
    {
        var block = RazorParse.Single<StatementBlock>("@{ @* not } code *@ var a = 1; }");

        _ = Assert.IsType<RazorComment>(block.Body[0]);
        Assert.Equal("var a = 1;", Assert.IsType<CodeRegion>(block.Body[1]).Text);
    }
}