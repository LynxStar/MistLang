using Mist.Common.Diagnostics;
using static Mist.Tests.Backend.Razor.TranslatorHarness;

namespace Mist.Tests.Backend.Razor;

public sealed partial class RazorComponentTranslatorTests
{
    [Fact]
    public void StaticMarkupFoldsIntoMarkupContentAsRazorFoldsIt()
    {
        var translation = Content(H.El("h1", H.Text("Counter")), H.Text("\n"), H.El("p", [H.Attr("class", "lead")], H.Text("Hi")));

        Assert.Equal(["__builder.AddMarkupContent(0, \"<h1>Counter</h1>\\n\");", "__builder.AddMarkupContent(1, \"<p class=\\\"lead\\\">Hi</p>\");"],
            translation.Calls());
    }

    [Fact]
    public void AStaticSelectWritesItsOptionsAsFrames()
    {
        var calls = Content(H.El("select", [H.Attr("class", "s")], H.El("option", [H.Attr("value", "x")], H.Text("x")))).Calls();

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"select\");",
                "__builder.AddAttribute(1, \"class\", \"s\");",
                "__builder.OpenElement(2, \"option\");",
                "__builder.AddAttribute(3, \"value\", \"x\");",
                "__builder.AddContent(4, \"x\");",
                "__builder.CloseElement();",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void ABoundSelectWritesItsOptionsAsFramesAfterTheBinder()
    {
        var tree = new HandTree()
            .Content(H.El("select", [H.Dir("bind", "x"), H.Attr("class", "s")],
                H.El("option", [H.Attr("value", "x")], H.Text("x")), H.El("option", H.Text("y"))))
            .Code("private string x = \"x\";")
            .Tree();

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"select\");",
                "__builder.AddAttribute(1, \"class\", \"s\");",
                "__builder.AddAttribute(2, \"value\", BindConverter.FormatValue(x));",
                "__builder.AddAttribute(3, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { x = __v; }, x));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.OpenElement(4, \"option\");",
                "__builder.AddAttribute(5, \"value\", \"x\");",
                "__builder.AddContent(6, \"x\");",
                "__builder.CloseElement();",
                "__builder.OpenElement(7, \"option\");",
                "__builder.AddContent(8, \"y\");",
                "__builder.CloseElement();",
                "__builder.CloseElement();",
            ],
            Run(tree).Calls());
    }

    [Fact]
    public void ADivStillFoldsItsOptionIntoMarkup()
    {
        var calls = Content(H.El("div", H.El("option", [H.Attr("value", "x")], H.Text("x")))).Calls();

        Assert.Equal(["__builder.AddMarkupContent(0, \"<div><option value=\\\"x\\\">x</option></div>\");"], calls);
    }

    [Fact]
    public void ASelectContextReachesNestedOptionsAndEndsBeforeTheFollowingDiv()
    {
        var calls = Content(
            H.El("select", H.El("optgroup", H.El("option", H.Text("x")))),
            H.El("div", H.El("option", H.Text("y")))).Calls();

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"select\");",
                "__builder.OpenElement(1, \"optgroup\");",
                "__builder.OpenElement(2, \"option\");",
                "__builder.AddContent(3, \"x\");",
                "__builder.CloseElement();",
                "__builder.CloseElement();",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(4, \"<div><option>y</option></div>\");",
            ],
            calls);
    }

    [Fact]
    public void AnElementHoldingATransitionOpensAndClosesAroundItsChildren()
    {
        var translation = Content(H.El("p", [H.Attr("role", "status")], H.Text("Current count: "), H.Expr("count")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"p\");",
                "__builder.AddAttribute(1, \"role\", \"status\");",
                "__builder.AddContent(2, \"Current count: \");",
                "__builder.AddContent(3, count);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void AnAttributeValueIsALiteralAnExpressionOrAConcatenation()
    {
        var translation = Content(H.El("a",
            [H.Attr("href", H.Expr("link")), H.Attr("title", H.Text("a "), H.Explicit("n + 1"), H.Text(" b")), H.Bare("hidden"), H.Attr("id", "x")],
            H.Expr("n")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"a\");",
                "__builder.AddAttribute(1, \"href\", link);",
                "__builder.AddAttribute(2, \"title\", \"a \" + (n + 1) + \" b\");",
                "__builder.AddAttribute(3, \"hidden\");",
                "__builder.AddAttribute(4, \"id\", \"x\");",
                "__builder.AddContent(5, n);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void ImplicitAndExplicitExpressionsAddTheirContent()
    {
        var translation = Content(H.El("p", H.Expr("user.Name"), H.Text(" "), H.Explicit("int.Name")));

        Assert.Equal(
            ["__builder.OpenElement(0, \"p\");", "__builder.AddContent(1, user.Name);", "__builder.AddContent(2, \" \");",
                "__builder.AddContent(3, int.Name);", "__builder.CloseElement();"],
            translation.Calls());
    }

    [Fact]
    public void TextWithAnEntityDecodesIntoContentAndTextWithALineBreakStaysMarkup()
    {
        var translation = Content(H.El("p", H.Text("a &amp; b "), H.Expr("x"), H.Text(" c\n")));

        Assert.Equal(
            ["__builder.OpenElement(0, \"p\");", "__builder.AddContent(1, \"a & b \");", "__builder.AddContent(2, x);",
                "__builder.AddMarkupContent(3, \" c\\n\");", "__builder.CloseElement();"],
            translation.Calls());
    }

    [Fact]
    public void AnEventAttributeCreatesACallbackOfTheCatalogsArgumentType()
    {
        var translation = Content(
            H.El("button", [H.Dir("onclick", "Increment"), H.Dir("onchange", "(ChangeEventArgs e) => Pick(e)"), H.Dir("onfocus", "Focus")], H.Expr("n")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"button\");",
                $"__builder.AddAttribute(1, \"onclick\", EventCallback.Factory.Create<{MouseEventArgs}>(this, Increment));",
                "__builder.AddAttribute(2, \"onchange\", EventCallback.Factory.Create<ChangeEventArgs>(this, (ChangeEventArgs e) => Pick(e)));",
                "__builder.AddAttribute(3, \"onfocus\", EventCallback.Factory.Create<System.EventArgs>(this, Focus));",
                "__builder.AddContent(4, n);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void PreventDefaultAndStopPropagationLowerToTheirAttributes()
    {
        var translation = Content(H.El("a",
            [H.Dir("onclick", "Go"), H.Dir("onclick", null, "preventDefault"), H.Dir("onclick", "stop", "stopPropagation")], H.Text("go")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"a\");",
                $"__builder.AddAttribute(1, \"onclick\", EventCallback.Factory.Create<{MouseEventArgs}>(this, Go));",
                "__builder.AddEventPreventDefaultAttribute(2, \"onclick\", true);",
                "__builder.AddEventStopPropagationAttribute(3, \"onclick\", stop);",
                "__builder.AddContent(4, \"go\");",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void KeyAndReferenceFollowTheAttributesAndAttributesSplatInPlace()
    {
        var translation = Content(H.El("div",
            [H.Dir("key", "item.Id"), H.Attr("class", "row"), H.Dir("ref", "panel"), H.Dir("attributes", "extra"), H.Attr("id", "last")],
            H.Expr("item")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"div\");",
                "__builder.AddAttribute(1, \"class\", \"row\");",
                "__builder.AddMultipleAttributes(2, extra);",
                "__builder.AddAttribute(3, \"id\", \"last\");",
                "__builder.SetKey(item.Id);",
                "__builder.AddElementReferenceCapture(4, (ElementReference __r) => { panel = __r; });",
                "__builder.AddContent(5, item);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void BindLowersToAFormattedValueAndABinderOfTheFieldsDeclaredType()
    {
        var tree = new HandTree()
            .Content(
                H.Void("input", H.Dir("bind", "name")),
                H.Void("input", H.Dir("bind", "name"), H.Dir("bind", "oninput", "event")),
                H.Void("input", H.Dir("bind", "date"), H.Dir("bind", "yyyy-MM-dd", "format"), H.Dir("bind", "culture", "culture")))
            .Code("private string name = \"\";\nprivate DateTime date = DateTime.Now;\nprivate CultureInfo culture = CultureInfo.InvariantCulture;")
            .Tree();

        var calls = Run(tree).Calls();

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"input\");",
                "__builder.AddAttribute(1, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(2, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
                "__builder.OpenElement(3, \"input\");",
                "__builder.AddAttribute(4, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(5, \"oninput\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
                "__builder.OpenElement(6, \"input\");",
                "__builder.AddAttribute(7, \"value\", BindConverter.FormatValue(date, \"yyyy-MM-dd\", culture));",
                "__builder.AddAttribute(8, \"onchange\", EventCallback.Factory.CreateBinder(this, (DateTime __v) => { date = __v; }, date, \"yyyy-MM-dd\", culture));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Theory]
    [InlineData("name", "string", null, null, null, "")]
    [InlineData("name", "string", "oninput", null, null, "")]
    [InlineData("date", "DateTime", null, "yyyy-MM-dd", null, ", \"yyyy-MM-dd\"")]
    [InlineData("date", "DateTime", null, null, "culture", ", culture")]
    [InlineData("date", "DateTime", null, "yyyy-MM-dd", "culture", ", \"yyyy-MM-dd\", culture")]
    public void ElementBindNamesTheValueAttributeItUpdatesAfterItsBinderWithNoSequence(
        string field, string type, string? changeEvent, string? format, string? culture, string passed)
    {
        HandAttribute?[] modifiers =
            [changeEvent is null ? null : H.Dir("bind", changeEvent, "event"), format is null ? null : H.Dir("bind", format, "format"),
                culture is null ? null : H.Dir("bind", culture, "culture")];
        var tree = new HandTree()
            .Content(H.Void("input", [H.Dir("bind", field), .. modifiers.OfType<HandAttribute>()]), H.Text("\n"), H.El("p", H.Expr(field)))
            .Code("private string name = \"\";\nprivate DateTime date = DateTime.Now;\nprivate CultureInfo culture = CultureInfo.InvariantCulture;")
            .Tree();

        var calls = Run(tree).Calls();

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"input\");",
                $"__builder.AddAttribute(1, \"value\", BindConverter.FormatValue({field}{passed}));",
                $"__builder.AddAttribute(2, \"{changeEvent ?? "onchange"}\", EventCallback.Factory.CreateBinder(this, ({type} __v) => {{ {field} = __v; }}, {field}{passed}));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(3, \"\\n\");",
                "__builder.OpenElement(4, \"p\");",
                $"__builder.AddContent(5, {field});",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void ElementBindNamesItsAttributeBeforeTheKeyAndTheReferenceCapture()
    {
        var tree = new HandTree()
            .Content(H.Void("input", H.Dir("bind", "name"), H.Dir("key", "name"), H.Dir("ref", "panel")))
            .Code("private string name = \"\";\nprivate ElementReference panel;")
            .Tree();

        var calls = Run(tree).Calls();

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"input\");",
                "__builder.AddAttribute(1, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(2, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.SetKey(name);",
                "__builder.AddElementReferenceCapture(3, (ElementReference __r) => { panel = __r; });",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void ElementBindFollowsTheAttributesAndEventsWrittenBeforeAndAfterIt()
    {
        var calls = BindCalls(H.Void("input", H.Attr("class", "a"), H.Dir("bind", "name"), H.Dir("onclick", "F"), H.Attr("id", "b")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"input\");",
                "__builder.AddAttribute(1, \"class\", \"a\");",
                $"__builder.AddAttribute(2, \"onclick\", EventCallback.Factory.Create<{MouseEventArgs}>(this, F));",
                "__builder.AddAttribute(3, \"id\", \"b\");",
                "__builder.AddAttribute(4, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(5, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void ElementBindFollowsAnAttributeWrittenAfterItAndPrecedesTheKeyAndTheReferenceCapture()
    {
        var calls = BindCalls(H.Void("input", H.Dir("bind", "name"), H.Dir("key", "name"), H.Dir("ref", "panel"), H.Attr("class", "a"), H.Dir("onclick", "F")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"input\");",
                "__builder.AddAttribute(1, \"class\", \"a\");",
                $"__builder.AddAttribute(2, \"onclick\", EventCallback.Factory.Create<{MouseEventArgs}>(this, F));",
                "__builder.AddAttribute(3, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(4, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.SetKey(name);",
                "__builder.AddElementReferenceCapture(5, (ElementReference __r) => { panel = __r; });",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void ElementBindFollowsSplattedAttributesAndTheAttributeAfterThem()
    {
        var calls = BindCalls(H.Void("input", H.Dir("bind", "name"), H.Dir("attributes", "extra"), H.Attr("title", "t")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"input\");",
                "__builder.AddMultipleAttributes(1, extra);",
                "__builder.AddAttribute(2, \"title\", \"t\");",
                "__builder.AddAttribute(3, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(4, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void SelectBindFollowsTheSelectsOtherAttributes()
    {
        var calls = BindCalls(H.El("select", [H.Dir("bind", "name"), H.Attr("class", "s")]));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"select\");",
                "__builder.AddAttribute(1, \"class\", \"s\");",
                "__builder.AddAttribute(2, \"value\", BindConverter.FormatValue(name));",
                "__builder.AddAttribute(3, \"onchange\", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));",
                "__builder.SetUpdatesAttributeName(\"value\");",
                "__builder.CloseElement();",
            ],
            calls);
    }

    [Fact]
    public void ControlBlocksBecomeMistStatementsAroundTheirMarkup()
    {
        var translation = Content(
            H.Control(H.Arm("if", "(n > 1)", H.El("b", H.Expr("n"))), H.Arm("else if", "(n == 1)", H.El("i", H.Text("one"))), H.Arm("else", null, H.Code("var zero = 0;"), H.El("u", H.Expr("zero")))),
            H.Control(H.Arm("foreach", "(var item in items)", H.El("li", H.Expr("item")))),
            H.Control(H.Arm("while", "(n < 3)", H.Code("n = n + 1;"))),
            H.Control(H.Arm("readonly", null, H.El("p", H.Expr("n")))));

        Assert.Equal(
            [
                "if (n > 1)", "{", "    __builder.OpenElement(0, \"b\");", "    __builder.AddContent(1, n);", "    __builder.CloseElement();", "}",
                "else if (n == 1)", "{", "    __builder.AddMarkupContent(2, \"<i>one</i>\");", "}",
                "else", "{", "    var zero = 0;", "    __builder.OpenElement(3, \"u\");", "    __builder.AddContent(4, zero);", "    __builder.CloseElement();", "}",
                "foreach (var item in items)", "{", "    __builder.OpenElement(5, \"li\");", "    __builder.AddContent(6, item);", "    __builder.CloseElement();", "}",
                "while (n < 3)", "{", "    n = n + 1;", "}",
                "readonly", "{", "    __builder.OpenElement(7, \"p\");", "    __builder.AddContent(8, n);", "    __builder.CloseElement();", "}",
            ],
            translation.Calls());
    }

    [Fact]
    public void AStatementBlockWritesItsCodeInlineSoItsLocalsReachLaterMarkup()
    {
        var translation = Content(H.Block(H.Code("var total = 2;")), H.El("p", H.Expr("total")));

        Assert.Equal(["var total = 2;", "__builder.OpenElement(0, \"p\");", "__builder.AddContent(1, total);", "__builder.CloseElement();"],
            translation.Calls());
    }

    [Fact]
    public void ALineTransitionAndATextTagAreMarkupInsideABlock()
    {
        var translation = Content(H.Block(H.Line(H.Text(" n is "), H.Expr("n")), H.TextTag(H.Text("plain "), H.Expr("n"))));

        Assert.Equal(
            ["__builder.AddContent(0, \" n is \");", "__builder.AddContent(1, n);", "__builder.AddMarkupContent(2, \"\\n\");",
                "__builder.AddContent(3, \"plain \");", "__builder.AddContent(4, n);"],
            translation.Calls());
    }

    [Fact]
    public void ARazorCommentAndAnHtmlCommentProduceNothing()
    {
        var translation = Content(H.El("p", H.Text("a"), H.RazorComment(" gone "), H.Text("b "), H.Expr("n")), H.Comment(" dropped "));

        Assert.Equal(
            ["__builder.OpenElement(0, \"p\");", "__builder.AddMarkupContent(1, \"ab \");", "__builder.AddContent(2, n);", "__builder.CloseElement();"],
            translation.Calls());
    }

    [Fact]
    public void AnElementAfterARazorCommentFoldsAsRazorFoldsItsSiblings()
    {
        var translation = Content(
            H.El("p", H.Text("a"), H.RazorComment(" x "), H.El("b", H.Text("c")), H.Text(" "), H.Expr("n")), H.Text("\n"),
            H.El("div", H.Text("a"), H.RazorComment(" x "), H.El("b", H.Text("c"))));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"p\");",
                "__builder.AddContent(1, \"a\");",
                "__builder.AddMarkupContent(2, \"<b>c</b> \");",
                "__builder.AddContent(3, n);",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(4, \"\\n\");",
                "__builder.AddMarkupContent(5, \"<div>a<b>c</b></div>\");",
            ],
            translation.Calls());
    }

    [Fact]
    public void TextsARazorCommentSplitAreTrimmedApartThenFoldAsMarkup()
    {
        var translation = Content(
            H.El("p", H.Text(" "), H.RazorComment(" c "), H.Text("b "), H.Expr("n")), H.Text("\n"),
            H.El("p", H.Expr("n"), H.Text(" "), H.RazorComment(" c "), H.Text("b")), H.Text("\n"),
            H.El("p", H.Text("a"), H.RazorComment(" c "), H.Text("b"), H.RazorComment(" d "), H.Text("c "), H.Expr("n")), H.Text("\n"),
            H.El("p", H.Text("a"), H.RazorComment(" c "), H.Text("&amp;b "), H.Expr("n")));

        Assert.Equal(
            [
                "__builder.OpenElement(0, \"p\");",
                "__builder.AddContent(1, \"b \");",
                "__builder.AddContent(2, n);",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(3, \"\\n\");",
                "__builder.OpenElement(4, \"p\");",
                "__builder.AddContent(5, n);",
                "__builder.AddMarkupContent(6, \" b\");",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(7, \"\\n\");",
                "__builder.OpenElement(8, \"p\");",
                "__builder.AddMarkupContent(9, \"abc \");",
                "__builder.AddContent(10, n);",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(11, \"\\n\");",
                "__builder.OpenElement(12, \"p\");",
                "__builder.AddMarkupContent(13, \"a&amp;b \");",
                "__builder.AddContent(14, n);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void AnHtmlCommentAtTheRootIsDroppedWithTheWhitespaceItLeavesAtTheEdge()
    {
        var translation = Content(
            H.Comment(" root "), H.Text("\n"), H.El("h1", H.Text("Title")), H.Text("\n"), H.Comment(" between "), H.Text("\n"), H.El("h2", H.Text("Sub")),
            H.Text("\n"), H.El("p", H.Expr("count")), H.Text("\n"), H.Comment(" end "));

        Assert.Equal(
            [
                "__builder.AddMarkupContent(0, \"<h1>Title</h1>\\n\\n\");",
                "__builder.AddMarkupContent(1, \"<h2>Sub</h2>\\n\");",
                "__builder.OpenElement(2, \"p\");",
                "__builder.AddContent(3, count);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void AnHtmlCommentInsideAnElementIsDroppedAndTheTextsBesideItFoldIntoMarkup()
    {
        var translation = Content(
            H.El("p", H.Text("static "), H.Comment(" in static "), H.Text(" text")), H.Text("\n"),
            H.El("p", H.Expr("count"), H.Text(" "), H.Comment(" c "), H.Text(" after")), H.Text("\n"),
            H.El("p", H.Text("a"), H.Comment(" c "), H.Expr("count")), H.Text("\n"),
            H.El("p", H.Comment(" only ")), H.Text("\n"),
            H.El("ul", H.Comment(" lead "), H.El("li", H.Expr("count")), H.Text(" "), H.Comment(" tail ")));

        Assert.Equal(
            [
                "__builder.AddMarkupContent(0, \"<p>static  text</p>\\n\");",
                "__builder.OpenElement(1, \"p\");",
                "__builder.AddContent(2, count);",
                "__builder.AddMarkupContent(3, \"  after\");",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(4, \"\\n\");",
                "__builder.OpenElement(5, \"p\");",
                "__builder.AddContent(6, \"a\");",
                "__builder.AddContent(7, count);",
                "__builder.CloseElement();",
                "__builder.AddMarkupContent(8, \"\\n<p></p>\\n\");",
                "__builder.OpenElement(9, \"ul\");",
                "__builder.OpenElement(10, \"li\");",
                "__builder.AddContent(11, count);",
                "__builder.CloseElement();",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void AnHtmlCommentInsideATextTagIsDropped()
    {
        var translation = Content(H.Block(H.TextTag(H.Text("a "), H.Comment(" in text "), H.Text(" "), H.Expr("count"), H.Text(" b"))), H.El("div", H.Expr("count")));

        Assert.Equal(
            [
                "__builder.AddMarkupContent(0, \"a  \");",
                "__builder.AddContent(1, count);",
                "__builder.AddContent(2, \" b\");",
                "__builder.OpenElement(3, \"div\");",
                "__builder.AddContent(4, count);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
    }

    [Fact]
    public void AnHtmlCommentInsideAnIfBodyIsDropped()
    {
        var translation = Content(
            H.If("(show)", H.Comment(" only ")), H.If("(show)", H.Comment(" first "), H.El("b", H.Text("x")), H.Comment(" after b "), H.El("i", H.Expr("count"))));

        Assert.Equal(
            [
                "if (show)", "{", "}",
                "if (show)", "{",
                "    __builder.AddMarkupContent(0, \"<b>x</b>\\n    \\n    \");",
                "    __builder.OpenElement(1, \"i\");",
                "    __builder.AddContent(2, count);",
                "    __builder.CloseElement();",
                "}",
            ],
            translation.Calls());
    }

    [Fact]
    public void AnHtmlCommentHoldingATransitionEvaluatesNothingAndKeepsTheLineBreakAfterIt()
    {
        var translation = Content(
            H.Block(H.Code("var z = 1;")), H.Text("\n"), H.Comment(" @DateTime.Now "), H.Text("\ntext after "), H.Expr("z"), H.Text("\n"),
            H.Comment(" @z "), H.Text("\n"), H.El("div", H.Expr("z")));

        Assert.Equal(
            [
                "var z = 1;",
                "__builder.AddMarkupContent(0, \"\\ntext after \");",
                "__builder.AddContent(1, z);",
                "__builder.AddMarkupContent(2, \"\\n\\n\");",
                "__builder.OpenElement(3, \"div\");",
                "__builder.AddContent(4, z);",
                "__builder.CloseElement();",
            ],
            translation.Calls());
        Assert.DoesNotContain("DateTime", translation.Text.Contents);
    }

    [Fact]
    public void SequenceNumbersIncreaseInSourceOrderThroughEveryFragment()
    {
        var catalog = Catalog(Component("App.Components.Panel", ("ChildContent", "RenderFragment?")));
        var translation = Content(catalog,
            H.El("Panel", H.El("p", H.Expr("a")), H.El("Panel", H.Expr("b"))), H.El("p", H.Expr("c")));

        var sequences = translation.Calls()
            .Select(call => System.Text.RegularExpressions.Regex.Match(call, @"\.(?:Open\w+(?:<\w+>)?|Add\w+)\((\d+)"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Enumerable.Range(0, 9), sequences);
        Assert.Contains(translation.Calls(), call => call.Trim() == "__b2.AddContent(6, b);");
    }

    [Fact]
    public void AnUnknownDirectiveAttributeIsRefusedAtItsName()
    {
        var translation = Content(H.El("p", [H.Dir("bogus", "x")], H.Expr("n")));

        var diagnostic = Assert.Single(translation.Diagnostics);
        Assert.Same(RazorDiagnostics.UnknownDirectiveAttribute, diagnostic.Descriptor);
        Assert.Equal("@bogus", translation.Original((SourceRange)diagnostic.Location));
        Assert.Null(translation.Unit);
    }

    private static string[] BindCalls(Hand element)
    {
        return Run(new HandTree().Content(element).Code("private string name = \"\";\nprivate ElementReference panel;").Tree()).Calls();
    }
}