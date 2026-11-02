using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Driver.Contracts;
using static Mist.Tests.Backend.Razor.TranslatorHarness;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "2")]
public sealed partial class RazorComponentTranslatorTests
{
    private const string FixtureFolder = "/fixture";

    private static readonly MistProject FixtureProject = new("RazorComponents", FixtureFolder, FixtureFolder + "/mist.toml");

    private static readonly ComponentCatalog FixtureCatalog = Catalog(
        new ComponentFacts("Microsoft.AspNetCore.Components.Web.PageTitle",
            [ParameterFacts.Declared("ChildContent", "Microsoft.AspNetCore.Components.RenderFragment")], 0, IsMist: false),
        Component("RazorComponents.Components.Greeting", ("Title", "string"), ("Count", "int"), ("ChildContent", "RenderFragment?")));

    [Fact]
    public void TheFixtureCounterTranslatesToItsRenderProtocol()
    {
        var counter = TranslateFixture("Counter.razor");

        Assert.Empty(counter.Diagnostics);
        Assert.Equal(
            """
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;
            using Microsoft.AspNetCore.Components.Web;
            using RazorComponents.Components;

            [Route("/counter")]
            public class Counter : ComponentBase
            {
                private int count = 0;
                private int? lastReset = void;

                private pure int Next(int value)
                {
                    return value + 1;
                }

                private void Increment()
                {
                    count = Next(count);
                }

                private void Reset()
                {
                    lastReset = count;
                    count = 0;
                }

                protected void BuildRenderTree(RenderTreeBuilder __builder)
                {
                    __builder.OpenComponent<PageTitle>(0);
                    __builder.AddComponentParameter(1, "ChildContent", (RenderFragment)((RenderTreeBuilder __b1) =>
                    {
                        __b1.AddContent(2, "Counter");
                    }));
                    __builder.CloseComponent();
                    __builder.AddMarkupContent(3, "\n\n");
                    __builder.AddMarkupContent(4, "<h1>Counter</h1>\n\n");
                    __builder.OpenElement(5, "p");
                    __builder.AddAttribute(6, "role", "status");
                    __builder.AddContent(7, "Current count: ");
                    __builder.AddContent(8, count);
                    __builder.CloseElement();
                    __builder.AddMarkupContent(9, "\n");
                    __builder.OpenElement(10, "p");
                    __builder.AddContent(11, "The count is a ");
                    __builder.AddContent(12, int.Name);
                    __builder.AddContent(13, ".");
                    __builder.CloseElement();
                    if (lastReset is void)
                    {
                        __builder.AddMarkupContent(14, "<p>Never reset.</p>");
                    }
                    else
                    {
                        __builder.OpenElement(15, "p");
                        __builder.AddContent(16, "Last reset at count ");
                        __builder.AddContent(17, lastReset);
                        __builder.AddContent(18, ".");
                        __builder.CloseElement();
                    }
                    __builder.OpenElement(19, "button");
                    __builder.AddAttribute(20, "class", "btn btn-primary");
                    __builder.AddAttribute(21, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Increment));
                    __builder.AddContent(22, "Click me");
                    __builder.CloseElement();
                    __builder.AddMarkupContent(23, "\n");
                    __builder.OpenElement(24, "button");
                    __builder.AddAttribute(25, "class", "btn btn-secondary");
                    __builder.AddAttribute(26, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Reset));
                    __builder.AddContent(27, "Reset");
                    __builder.CloseElement();
                    __builder.AddMarkupContent(28, "\n\n");
                    __builder.OpenComponent<Greeting>(29);
                    __builder.AddComponentParameter(30, "Title", "Counter says");
                    __builder.AddComponentParameter(31, "Count", count);
                    __builder.AddComponentParameter(32, "ChildContent", (RenderFragment)((RenderTreeBuilder __b2) =>
                    {
                        __b2.AddMarkupContent(33, "<em>Nested from the counter.</em>");
                    }));
                    __builder.CloseComponent();
                }
            }

            """.ReplaceLineEndings("\n"),
            counter.Text.Contents);
    }

    [Fact]
    public void TheFixtureGreetingNarrowsItsChildContentInAMistIf()
    {
        var greeting = TranslateFixture("Greeting.razor");

        Assert.Empty(greeting.Diagnostics);
        Assert.Equal(
            """
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;
            using Microsoft.AspNetCore.Components.Web;
            using RazorComponents.Components;

            public class Greeting : ComponentBase
            {
                [Parameter]
                public string Title { get; set; } = "Hello";

                [Parameter]
                public int Count { get; set; }

                [Parameter]
                public RenderFragment? ChildContent { get; set; }

                protected void BuildRenderTree(RenderTreeBuilder __builder)
                {
                    __builder.OpenElement(0, "section");
                    __builder.AddAttribute(1, "class", "greeting");
                    __builder.OpenElement(2, "h2");
                    __builder.AddContent(3, Title);
                    __builder.CloseElement();
                    if (ChildContent is RenderFragment body)
                    {
                        __builder.OpenElement(4, "div");
                        __builder.AddAttribute(5, "class", "body");
                        __builder.AddContent(6, body);
                        __builder.CloseElement();
                    }
                    __builder.OpenElement(7, "p");
                    __builder.AddContent(8, "Shown ");
                    __builder.AddContent(9, Count);
                    __builder.AddContent(10, " times.");
                    __builder.CloseElement();
                    __builder.CloseElement();
                }
            }

            """.ReplaceLineEndings("\n"),
            greeting.Text.Contents);
    }

    [Fact]
    public void ACodeRegionMapsToItsComponentFileSpan()
    {
        var counter = TranslateFixture("Counter.razor");

        var expression = counter.Map("int.Name");
        var header = counter.Map("lastReset is void");
        var member = counter.Map("private pure int Next");

        Assert.Equal("int.Name", counter.Original(expression));
        Assert.Equal("lastReset is void", counter.Original(header));
        Assert.Equal("private pure int Next", counter.Original(member));
        Assert.Equal(FixtureFolder + "/Components/Counter.razor", expression.File.Text.Path);
    }

    [Fact]
    public void ASynthesizedCallMapsToTheConstructItLowers()
    {
        var counter = TranslateFixture("Counter.razor");

        Assert.Equal("<p role=\"status\">", counter.Original(counter.Map("__builder.OpenElement(5")));
        Assert.Equal("role=\"status\"", counter.Original(counter.Map("__builder.AddAttribute(6, \"role\", \"status\");")));
        Assert.Equal("@onclick=\"Increment\"", counter.Original(counter.Map("EventCallback.Factory.Create<MouseEventArgs>(this, ")));
        Assert.Equal("@page \"/counter\"", counter.Original(counter.Map("[Route(")));
    }

    [Fact]
    public void AnImportedUsingMapsIntoTheImportsFile()
    {
        var counter = TranslateFixture("Counter.razor");

        var range = counter.Map("Microsoft.AspNetCore.Components.Web;");

        Assert.Equal(FixtureFolder + "/Components/_Imports.razor", range.File.Text.Path);
        Assert.Equal("Microsoft.AspNetCore.Components.Web", range.File.Text.Contents.Substring(range.Offset, range.Length));
    }

    [Fact]
    public void TheOriginMapCoversEveryCharacterOfTheUnit()
    {
        var counter = TranslateFixture("Counter.razor");

        Assert.Equal(counter.Text.Contents.Length, counter.Text.Origin!.Length);
        Assert.True(counter.Text.Origin.Covers(counter.Text.Contents));
    }

    private static Translation TranslateFixture(string fileName)
    {
        var imports = Parse(FixtureFolder + "/Components/_Imports.razor", RazorParse.Fixture("_Imports.razor"));
        var tree = Parse(FixtureFolder + "/Components/" + fileName, RazorParse.Fixture(fileName));
        return Run(tree, FixtureCatalog, FixtureProject, imports);
    }
}