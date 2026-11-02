using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Common.Diagnostics;
using static Mist.Tests.Backend.Razor.TranslatorHarness;

namespace Mist.Tests.Backend.Razor;

public sealed partial class RazorComponentTranslatorTests
{
    private static readonly ComponentCatalog Widgets = Catalog(
        Component("App.Components.Card", ("Title", "string?"), ("Count", "int"), ("Picked", "EventCallback<int>"), ("ChildContent", "RenderFragment?")),
        Component("App.Components.Grid", ("Header", "RenderFragment?"), ("Row", "RenderFragment<Item>?")),
        Component("App.Components.List", ("ChildContent", "RenderFragment<string>?")),
        Component("App.Components.Field", ("Value", "int"), ("ValueExpression", "Expression<Func<int>>")),
        Component("App.Shared.Card"),
        new ComponentFacts("App.Components.Table", [], 1, IsMist: true));

    [Fact]
    public void AComponentAttributeIsALiteralForAStringParameterAndAnExpressionOtherwise()
    {
        var translation = Content(Widgets, H.Void("Card",
            H.Attr("Title", "Counter says"), H.Attr("Count", "count"), H.Attr("Picked", "Pick"), H.Attr("data-x", "free"), H.Attr("Count", H.Expr("other")), H.Bare("Flag")));

        Assert.Equal(
            [
                "__builder.OpenComponent<Card>(0);",
                "__builder.AddComponentParameter(1, \"Title\", \"Counter says\");",
                "__builder.AddComponentParameter(2, \"Count\", count);",
                "__builder.AddComponentParameter(3, \"Picked\", EventCallback.Factory.Create<int>(this, Pick));",
                "__builder.AddComponentParameter(4, \"data-x\", \"free\");",
                "__builder.AddComponentParameter(5, \"Count\", other);",
                "__builder.AddComponentParameter(6, \"Flag\", true);",
                "__builder.CloseComponent();",
            ],
            translation.Calls());
    }

    [Fact]
    public void ChildContentIsARenderFragmentCastFromALambda()
    {
        var translation = Content(Widgets, H.El("Card", H.Text("\n    "), H.El("em", H.Text("Nested")), H.Text("\n")));

        Assert.Equal(
            [
                "__builder.OpenComponent<Card>(0);",
                "__builder.AddComponentParameter(1, \"ChildContent\", (RenderFragment)((RenderTreeBuilder __b1) =>",
                "{",
                "    __b1.AddMarkupContent(2, \"<em>Nested</em>\");",
                "}));",
                "__builder.CloseComponent();",
            ],
            translation.Calls());
    }

    [Fact]
    public void ANamedTemplateLowersToItsParameterWithItsContextName()
    {
        var translation = Content(Widgets, H.El("Grid",
            H.El("Header", H.Text("Items")),
            H.El("Row", [H.Attr("Context", "item")], H.El("td", H.Expr("item.Name")))));

        Assert.Equal(
            [
                "__builder.OpenComponent<Grid>(0);",
                "__builder.AddComponentParameter(1, \"Header\", (RenderFragment)((RenderTreeBuilder __b1) =>",
                "{",
                "    __b1.AddContent(2, \"Items\");",
                "}));",
                "__builder.AddComponentParameter(3, \"Row\", (RenderFragment<Item>)((Item item) => (RenderTreeBuilder __b2) =>",
                "{",
                "    __b2.OpenElement(4, \"td\");",
                "    __b2.AddContent(5, item.Name);",
                "    __b2.CloseElement();",
                "}));",
                "__builder.CloseComponent();",
            ],
            translation.Calls());
    }

    [Fact]
    public void ATemplatedChildContentNamesItsValueContextByDefault()
    {
        var translation = Content(Widgets, H.El("List", H.El("b", H.Expr("context"))));

        Assert.Equal("__builder.AddComponentParameter(1, \"ChildContent\", (RenderFragment<string>)((string context) => (RenderTreeBuilder __b1) =>",
            translation.Calls()[1]);
    }

    [Fact]
    public void KeyReferenceAndRenderModeFollowTheParametersOfAComponent()
    {
        var translation = Content(Widgets, H.Void("Card",
            H.Dir("key", "id"), H.Dir("rendermode", "RenderMode.InteractiveServer"), H.Attr("Count", "count"), H.Dir("ref", "card"), H.Dir("attributes", "rest")));

        Assert.Equal(
            [
                "__builder.OpenComponent<Card>(0);",
                "__builder.AddComponentParameter(1, \"Count\", count);",
                "__builder.AddMultipleAttributes(2, rest);",
                "__builder.SetKey(id);",
                "__builder.AddComponentReferenceCapture(3, (System.Object __r) => { if (__r is Card __c) { card = __c; } });",
                "__builder.AddComponentRenderMode(RenderMode.InteractiveServer);",
                "__builder.CloseComponent();",
            ],
            translation.Calls());
    }

    [Fact]
    public void BindParameterPassesTheValueAndAChangedCallbackOfTheParameterType()
    {
        var translation = Content(Widgets, H.Void("Card", H.Dir("bind-Count", "count")));

        Assert.Equal(
            [
                "__builder.OpenComponent<Card>(0);",
                "__builder.AddComponentParameter(1, \"Count\", count);",
                "__builder.AddComponentParameter(2, \"CountChanged\", EventCallback.Factory.Create<int>(this, (int __v) => { count = __v; }));",
                "__builder.CloseComponent();",
            ],
            translation.Calls());
    }

    [Fact]
    public void BindParameterBesideAnExpressionParameterIsRefused()
    {
        var translation = Content(Widgets, H.Void("Field", H.Dir("bind-Value", "value")));

        var diagnostic = Assert.Single(translation.Diagnostics);
        Assert.Same(RazorDiagnostics.ExpressionParameterNotSupported, diagnostic.Descriptor);
        Assert.Equal("@bind-Value", translation.Original((SourceRange)diagnostic.Location));
    }

    [Fact]
    public void AnUnknownComponentAGenericOneAndAnAmbiguousOneAreRefusedAtTheirTags()
    {
        var translation = Run(new HandTree()
            .Directive("using", "App.Shared")
            .Content(H.Void("Widget"), H.Void("Table"), H.Void("Card"))
            .Tree(), Widgets);

        Assert.Equal(
            [RazorDiagnostics.UnknownComponent, RazorDiagnostics.GenericComponentNotSupported, RazorDiagnostics.AmbiguousComponentName],
            translation.Diagnostics.Select(diagnostic => diagnostic.Descriptor));
        Assert.Equal(["Widget", "Table", "Card"], translation.Diagnostics.Select(diagnostic => translation.Original((SourceRange)diagnostic.Location)));
        Assert.Null(translation.Unit);
    }

    [Fact]
    public void ALowercaseTagWithNoComponentIsAnElement()
    {
        var translation = Content(Widgets, H.El("card", H.Expr("n")));

        Assert.Empty(translation.Diagnostics);
        Assert.Equal("__builder.OpenElement(0, \"card\");", translation.Calls()[0]);
    }
}