using Mist.Common.Diagnostics;
using static Mist.Tests.Backend.Razor.TranslatorHarness;

namespace Mist.Tests.Backend.Razor;

public sealed partial class RazorComponentTranslatorTests
{
    [Fact]
    public void PageLayoutAndAttributeDirectivesDecorateTheClass()
    {
        var translation = Run(new HandTree()
            .Directive("page", "\"/counter\"")
            .Directive("page", "\"/counter/{start:int}\"")
            .Directive("layout", "MainLayout")
            .Directive("attribute", "[Authorize]")
            .Tree());

        Assert.Equal(
            """
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;

            [Route("/counter")]
            [Route("/counter/{start:int}")]
            [Layout(MainLayout)]
            [Authorize]
            public class Probe : ComponentBase
            {
                protected void BuildRenderTree(RenderTreeBuilder __builder)
                {
                }
            }

            """,
            translation.Text.Contents);
    }

    [Fact]
    public void InheritsReplacesTheBaseAndImplementsAppendsToTheList()
    {
        var translation = Run(new HandTree()
            .Directive("inherits", "LayoutComponentBase")
            .Directive("implements", "IDisposable")
            .Directive("implements", "IAsyncDisposable")
            .Tree());

        Assert.Contains("public class Probe : LayoutComponentBase, IDisposable, IAsyncDisposable\n", translation.Text.Contents);
    }

    [Fact]
    public void InjectBecomesAReadonlyFieldAssignedByOneSynthesizedConstructor()
    {
        var translation = Run(new HandTree()
            .Directive("inject", "NavigationManager Nav")
            .Directive("inject", "IJSRuntime JS")
            .Code("private int count = 0;")
            .Tree());

        Assert.Contains(
            """
            public class Probe : ComponentBase
            {
                private readonly NavigationManager Nav;
                private readonly IJSRuntime JS;

                public Probe(NavigationManager Nav, IJSRuntime JS)
                {
                    this.Nav = Nav;
                    this.JS = JS;
                }

                private int count = 0;

            """,
            translation.Text.Contents);
        Assert.DoesNotContain("[Inject]", translation.Text.Contents);
        Assert.Equal("@inject IJSRuntime JS", translation.Original(translation.Map("this.JS = ")));
    }

    [Fact]
    public void AConstructorBesideInjectIsRefusedAtTheConstructor()
    {
        var translation = Run(new HandTree()
            .Directive("inject", "NavigationManager Nav")
            .Code("public Probe(int seed)\n{\n}")
            .Tree());

        var diagnostic = Assert.Single(translation.Diagnostics);
        Assert.Same(RazorDiagnostics.ConstructorBesideInject, diagnostic.Descriptor);
        Assert.Equal("Probe", translation.Original((SourceRange)diagnostic.Location));
    }

    [Fact]
    public void UsingsComeFromTheImportsFilesOutermostFirstThenTheFile()
    {
        var root = new HandTree().Directive("using", "App.Shared").Directive("layout", "OuterLayout").Tree("/app/_Imports.razor");
        var inner = new HandTree().Directive("using", "App.Components.Forms").Directive("layout", "InnerLayout").Tree("/app/Components/_Imports.razor");
        var tree = new HandTree().Directive("using", "System.Text;").Directive("using", "App.Shared").Tree();

        var translation = Run(tree, null, null, inner, root);

        Assert.StartsWith(
            """
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;
            using App.Shared;
            using App.Components.Forms;
            using System.Text;

            [Layout(InnerLayout)]

            """,
            translation.Text.Contents);
    }

    [Fact]
    public void UsingStaticAndUsingAliasAreRefused()
    {
        var translation = Run(new HandTree()
            .Directive("using", "static Microsoft.AspNetCore.Components.Web.RenderMode")
            .Directive("using", "Json = System.Text.Json.JsonSerializer")
            .Tree());

        Assert.Equal([RazorDiagnostics.UsingFormNotSupported, RazorDiagnostics.UsingFormNotSupported],
            translation.Diagnostics.Select(diagnostic => diagnostic.Descriptor));
    }

    [Fact]
    public void NamespaceReplacesTheInferredNamespace()
    {
        var translation = Run(new HandTree().Directive("namespace", "App.Pages").Tree());

        Assert.Contains("using Microsoft.AspNetCore.Components.Rendering;\n\nnamespace App.Pages;\n\npublic class Probe", translation.Text.Contents);
    }

    [Fact]
    public void RenderModeIsANestedAttributeClassFillingMode()
    {
        var translation = Run(new HandTree().Directive("rendermode", "RenderMode.InteractiveServer").Tree());

        Assert.Contains(
            """
            [Probe.__RenderModeAttribute]
            public class Probe : ComponentBase
            {
                private class __RenderModeAttribute : RenderModeAttribute
                {
                    public IComponentRenderMode Mode => RenderMode.InteractiveServer;
                }

            """,
            translation.Text.Contents);
    }

    [Fact]
    public void TwoCodeBlocksAreCopiedVerbatimInOrder()
    {
        var translation = Run(new HandTree().Code("private int first = 1;").Code("private int second = 2;").Tree());

        Assert.Contains("{\n    private int first = 1;\n\n    private int second = 2;\n\n    protected void BuildRenderTree", translation.Text.Contents);
        Assert.Equal("private int second = 2;", translation.Original(translation.Map("private int second = 2;")));
    }

    [Fact]
    public void LaterWorkDirectivesAreRefusedAtTheirKeyword()
    {
        var translation = Run(new HandTree().Directive("typeparam", "TItem").Directive("preservewhitespace", "true").Tree());

        Assert.Equal([RazorDiagnostics.DirectiveNotSupportedYet, RazorDiagnostics.DirectiveNotSupportedYet],
            translation.Diagnostics.Select(diagnostic => diagnostic.Descriptor));
        Assert.Equal(["@typeparam", "@preservewhitespace"], translation.Diagnostics.Select(diagnostic => translation.Original((SourceRange)diagnostic.Location)));
    }

    [Fact]
    public void ATypeInACodeBlockAndAReservedNameAreRefusedAtTheirNames()
    {
        var translation = Run(new HandTree().Code("private int __count = 0;\nclass Helper\n{\n}\nenum Mode { A }").Tree());

        Assert.Equal(
            [RazorDiagnostics.ReservedIdentifier, RazorDiagnostics.NestedTypeInCodeBlock, RazorDiagnostics.NestedTypeInCodeBlock],
            translation.Diagnostics.Select(diagnostic => diagnostic.Descriptor));
        Assert.Equal(["__count", "Helper", "Mode"], translation.Diagnostics.Select(diagnostic => translation.Original((SourceRange)diagnostic.Location)));
    }

    [Fact]
    public void ANamespaceInACodeBlockIsRefusedAsANestedDeclaration()
    {
        var translation = Run(new HandTree().Code("namespace Inner { }").Tree());

        var diagnostic = Assert.Single(translation.Diagnostics);
        Assert.Same(RazorDiagnostics.NestedTypeInCodeBlock, diagnostic.Descriptor);
        Assert.Equal("Inner", translation.Original((SourceRange)diagnostic.Location));
    }
}