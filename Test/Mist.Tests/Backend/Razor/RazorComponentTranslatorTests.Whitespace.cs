using static Mist.Tests.Backend.Razor.TranslatorHarness;

namespace Mist.Tests.Backend.Razor;

public sealed partial class RazorComponentTranslatorTests
{
    [Theory]
    [InlineData("text", "\n", "\\n")]
    [InlineData("blank", "\n", "\\n")]
    [InlineData("element", "\n", "\\n")]
    [InlineData("expression", "\n", "\\n")]
    [InlineData("comment", "\n", "\\n")]
    [InlineData("text", "\r\n", "\\r\\n")]
    [InlineData("blank", "\r\n", "\\r\\n")]
    [InlineData("element", "\r\n", "\\r\\n")]
    [InlineData("expression", "\r\n", "\\r\\n")]
    [InlineData("comment", "\r\n", "\\r\\n")]
    public void ATrailingCodeBlockAddsNoWhitespaceToTheLastMarkupCall(string shape, string newline, string escaped)
    {
        Hand[] markup = shape switch
        {
            "element" => [H.El("p", H.Text("a")), H.Text(newline)],
            "expression" => [H.Expr("x"), H.Text(newline)],
            "comment" => [H.El("span", H.Text("x")), H.Text(" y"), H.RazorComment("gone"), H.Text("z" + newline)],
            _ => [H.El("span", H.Text("x")), H.Text(" y" + newline + (shape == "blank" ? newline : ""))],
        };
        var tree = new HandTree().Content(markup)
            .Code("private string x = \"x\";", leading: "", trailing: "").Content(H.Text(newline)).Tree();

        string[] expected = shape switch
        {
            "element" => ["__builder.AddMarkupContent(0, \"<p>a</p>\");"],
            "expression" => ["__builder.AddContent(0, x);"],
            "comment" => [$"__builder.AddMarkupContent(0, \"<span>x</span> yz{escaped}\");"],
            _ => [$"__builder.AddMarkupContent(0, \"<span>x</span> y{escaped}{(shape == "blank" ? escaped : "")}\");"],
        };
        Assert.Equal(expected, Run(tree).Calls());
    }

    [Theory]
    [MemberData(nameof(MemberBlockShapes.Rows), MemberType = typeof(MemberBlockShapes))]
    public void AMemberBlockLeavesTheMarkupCallsRazorWrites(string shape, string newline)
    {
        var e = newline.Replace("\r", "\\r").Replace("\n", "\\n");

        string[] expected = shape switch
        {
            "element" => [$"__builder.AddMarkupContent(0, \"<p>a</p>{e}\");", "__builder.AddMarkupContent(1, \"<p>b</p>\");"],
            "textExpression" => [$"__builder.AddMarkupContent(0, \"<p>a</p> t{e}\");", "__builder.AddContent(1, x);"],
            "text" or "indented" => [$"__builder.AddMarkupContent(0, \"a{e}b{e}\");"],
            "blank" => [$"__builder.AddMarkupContent(0, \"a{e}{e}{e}b{e}\");"],
            "expressionElement" => ["__builder.AddContent(0, x);", $"__builder.AddMarkupContent(1, \"{e}\");", "__builder.AddMarkupContent(2, \"<p>b</p>\");"],
            "start" => [$"__builder.AddMarkupContent(0, \"b{e}\");"],
            "comment" => [$"__builder.AddMarkupContent(0, \"<span>x</span> yz{e}b{e}\");"],
            "trailingBlank" => [$"__builder.AddMarkupContent(0, \"<span>x</span> y{e}\");"],
            _ => [$"__builder.AddMarkupContent(0, \"a{e}{e}b{e}\");"],
        };
        Assert.Equal(expected, Run(MemberBlockShapes.Tree(shape, newline)).Calls());
    }
}