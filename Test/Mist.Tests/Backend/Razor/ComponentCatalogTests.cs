using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class ComponentCatalogTests
{
    private static readonly ComponentFacts Greeting = Mist("App.Components.Greeting");
    private static readonly ComponentFacts SharedWidget = Mist("App.Shared.Widget");
    private static readonly ComponentFacts ToolsWidget = Mist("App.Tools.Widget");
    private static readonly ComponentFacts Router = new("Microsoft.AspNetCore.Components.Routing.Router", [], 0, IsMist: false);

    [Fact]
    public void OneComponentInScopeIsTheComponent()
    {
        var resolution = Catalog().Resolve("Greeting", Scope("App", "App.Components"));

        Assert.Equal(ResolutionKind.Component, resolution.Kind);
        Assert.Same(Greeting, resolution.Component);
    }

    [Fact]
    public void AQualifiedTagNamesItsComponentWithNoNamespaceInScope()
    {
        var resolution = Catalog().Resolve("App.Shared.Widget", Scope());

        Assert.Same(SharedWidget, resolution.Component);
    }

    [Fact]
    public void AnUppercaseTagWithNoComponentInScopeIsUnknown()
    {
        var resolution = Catalog().Resolve("Greeting", Scope("App"));

        Assert.Equal(ResolutionKind.Unknown, resolution.Kind);
        Assert.Empty(resolution.Candidates);
        Assert.Null(resolution.Component);
    }

    [Theory]
    [InlineData("div")]
    [InlineData("greeting")]
    [InlineData("")]
    public void ATagThatIsNotUppercaseAndResolvesToNothingIsAnElement(string tagName)
    {
        var resolution = Catalog().Resolve(tagName, Scope("App.Components"));

        Assert.Equal(ResolutionKind.Element, resolution.Kind);
        Assert.Empty(resolution.Candidates);
    }

    [Fact]
    public void TwoComponentsOfTheNameInScopeAreAmbiguous()
    {
        var resolution = Catalog().Resolve("Widget", Scope("App.Shared", "App.Tools"));

        Assert.Equal(ResolutionKind.Ambiguous, resolution.Kind);
        Assert.Equal([SharedWidget.FullName, ToolsWidget.FullName], resolution.Candidates.Select(candidate => candidate.FullName));
        Assert.Null(resolution.Component);
    }

    [Fact]
    public void ANamespaceInScopeTwiceYieldsOneCandidate()
    {
        var resolution = Catalog().Resolve("Widget", Scope("App.Shared", "App.Shared"));

        Assert.Same(SharedWidget, resolution.Component);
    }

    [Fact]
    public void AClrComponentResolvesThroughTheLookup()
    {
        var asked = new List<string>();

        var resolution = Catalog(asked).Resolve("Router", Scope("App", "Microsoft.AspNetCore.Components.Routing"));

        Assert.Same(Router, resolution.Component);
        Assert.Equal(["Router", "App.Router", "Microsoft.AspNetCore.Components.Routing.Router"], asked);
    }

    [Fact]
    public void AMistAndAClrComponentOfTheNameInScopeAreAmbiguous()
    {
        var clrWidget = new ComponentFacts("Vendor.Widget", [], 0, IsMist: false);
        var catalog = new ComponentCatalog([SharedWidget], name => name == clrWidget.FullName ? clrWidget : null, []);

        var resolution = catalog.Resolve("Widget", Scope("App.Shared", "Vendor"));

        Assert.Equal([SharedWidget.FullName, clrWidget.FullName], resolution.Candidates.Select(candidate => candidate.FullName));
    }

    [Fact]
    public void AMistComponentIsFoundBeforeTheLookupIsAsked()
    {
        var asked = new List<string>();

        _ = Catalog(asked).Find(Greeting.FullName);

        Assert.Empty(asked);
    }

    [Theory]
    [InlineData("string", nameof(ParameterShape.String))]
    [InlineData("string?", nameof(ParameterShape.String))]
    [InlineData("RenderFragment", nameof(ParameterShape.RenderFragment))]
    [InlineData("RenderFragment?", nameof(ParameterShape.RenderFragment))]
    [InlineData("Microsoft.AspNetCore.Components.RenderFragment", nameof(ParameterShape.RenderFragment))]
    [InlineData("RenderFragment<Item>", nameof(ParameterShape.TemplatedFragment))]
    [InlineData("RenderFragment< List<int> >?", nameof(ParameterShape.TemplatedFragment))]
    [InlineData("Microsoft.AspNetCore.Components.RenderFragment<string>", nameof(ParameterShape.TemplatedFragment))]
    [InlineData("EventCallback", nameof(ParameterShape.EventCallback))]
    [InlineData("EventCallback<MouseEventArgs>", nameof(ParameterShape.EventCallback))]
    [InlineData("Microsoft.AspNetCore.Components.EventCallback<int>", nameof(ParameterShape.EventCallback))]
    [InlineData("int", nameof(ParameterShape.Plain))]
    [InlineData("Title", nameof(ParameterShape.Plain))]
    [InlineData("string[]", nameof(ParameterShape.Plain))]
    [InlineData("RenderFragment[]", nameof(ParameterShape.Plain))]
    [InlineData("RenderFragment<int>[]", nameof(ParameterShape.Plain))]
    [InlineData("Other.RenderFragment", nameof(ParameterShape.Plain))]
    [InlineData("List<EventCallback>", nameof(ParameterShape.Plain))]
    public void AParameterShapeIsDecidedFromItsTypeText(string typeText, string shape)
    {
        var expected = Enum.Parse<ParameterShape>(shape);

        Assert.Equal(expected, ParameterShapes.Of(typeText));
        Assert.Equal(new ParameterFacts("P", typeText, expected), ParameterFacts.Declared("P", typeText));
    }

    [Fact]
    public void AKnownEventNameTakesItsArgumentType()
    {
        var catalog = new ComponentCatalog([], _ => null, [new EventFacts("onclick", "Microsoft.AspNetCore.Components.Web.MouseEventArgs")]);

        Assert.Equal("Microsoft.AspNetCore.Components.Web.MouseEventArgs", catalog.EventArgumentType("onclick"));
    }

    [Theory]
    [InlineData("oncustom")]
    [InlineData("onClick")]
    public void AnEventNameTheCatalogDoesNotRecordFallsBackToEventArgs(string eventName)
    {
        var catalog = new ComponentCatalog([], _ => null, [new EventFacts("onclick", "Microsoft.AspNetCore.Components.Web.MouseEventArgs")]);

        Assert.Equal("System.EventArgs", catalog.EventArgumentType(eventName));
    }

    private static ComponentCatalog Catalog(List<string>? asked = null)
    {
        return new ComponentCatalog([Greeting, SharedWidget, ToolsWidget], name =>
        {
            asked?.Add(name);
            return name == Router.FullName ? Router : null;
        }, []);
    }

    private static ComponentFacts Mist(string fullName)
    {
        return new ComponentFacts(fullName, [ParameterFacts.Declared("Title", "string")], 0, IsMist: true);
    }

    private static ComponentScope Scope(params string[] namespaces)
    {
        return new ComponentScope([.. namespaces]);
    }
}