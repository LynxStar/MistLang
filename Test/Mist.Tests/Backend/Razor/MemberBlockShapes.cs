using Mist.Backends.CSharpTranspiler.Razor.Syntax;

namespace Mist.Tests.Backend.Razor;

/// <summary>
/// Components with a <c>@code</c> block between or beside markup runs, each measured against the
/// Razor compiler's generated C#; <c>design.md</c>, Detailed Design 6, whitespace rule 4, records them.
/// </summary>
internal static class MemberBlockShapes
{
    private const string X = "private string x = \"x\";";
    private const string Y = "private string y = \"y\";";

    public static TheoryData<string, string> Rows
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach(var newline in new[] { "\n", "\r\n" })
            {
                foreach(var shape in new[] { "element", "textExpression", "text", "blank", "expressionElement", "indented", "start", "comment", "trailingBlank", "twoBlocks" })
                {
                    rows.Add(shape, newline);
                }
            }

            return rows;
        }
    }

    public static RazorSyntaxTree Tree(string shape, string n)
    {
        var tree = new HandTree();
        return (shape switch
        {
            "element" => tree.Content(H.El("p", H.Text("a")), H.Text(n)).Code(X, "", "").Content(H.Text(n), H.El("p", H.Text("b")), H.Text(n)),
            "textExpression" => tree.Content(H.El("p", H.Text("a")), H.Text(" t" + n)).Code(X, "", "").Content(H.Text(n), H.Expr("x")),
            "text" => tree.Content(H.Text("a" + n)).Code(X, "", "").Content(H.Text(n + "b" + n)),
            "blank" => tree.Content(H.Text("a" + n + n)).Code(X, "", "").Content(H.Text(n + n + "b" + n)),
            "expressionElement" => tree.Content(H.Expr("x"), H.Text(n)).Code(X, "", "").Content(H.Text(n), H.El("p", H.Text("b")), H.Text(n)),
            "indented" => tree.Content(H.Text("a" + n + "  ")).Code(X, "", "").Content(H.Text("  " + n + "b" + n)),
            "start" => tree.Code(X, "", "").Content(H.Text(n + "b" + n)),
            "comment" => tree.Content(H.El("span", H.Text("x")), H.Text(" y"), H.RazorComment("c"), H.Text("z" + n)).Code(X, "", "").Content(H.Text(n + "b" + n)),
            "trailingBlank" => tree.Content(H.El("span", H.Text("x")), H.Text(" y" + n)).Code(X, "", "").Content(H.Text(n + n)),
            "twoBlocks" => tree.Content(H.Text("a" + n)).Code(X, "", "").Content(H.Text(n + n)).Code(Y, "", "").Content(H.Text(n + "b" + n)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        }).Tree();
    }
}