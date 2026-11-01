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

    [Theory]
    [InlineData("private int Twice(int __n)\n{\n    return __n * 2;\n}", "__n")]
    [InlineData("private void Go()\n{\n    var __total = 1;\n}", "__total")]
    [InlineData("private void Go()\n{\n    int __twice(int x)\n    {\n        return x * 2;\n    }\n}", "__twice")]
    [InlineData("private Func<int, int> twice = (__x) => __x * 2;", "__x")]
    [InlineData("private bool Check(object value)\n{\n    return value is string __s;\n}", "__s")]
    public void AReservedNameDeclaredInsideACodeBlockMemberIsRefusedWhereItIsDeclared(string code, string name)
    {
        var translation = Run(new HandTree().Code(code).Tree());

        AssertReservedAtDeclaration(translation, name);
    }

    [Theory]
    [InlineData("local in a block", "__total")]
    [InlineData("local function in a block", "__twice")]
    [InlineData("lambda parameter in a block", "__y")]
    [InlineData("foreach variable", "__item")]
    [InlineData("pattern variable in an if", "__s")]
    public void AReservedNameDeclaredInMarkupCodeIsRefusedWhereItIsDeclared(string kind, string name)
    {
        var markup = kind switch
        {
            "local in a block" => H.Block(H.Code("var __total = 2;")),
            "local function in a block" => H.Block(H.Code("int __twice(int x)\n    {\n        return x * 2;\n    }")),
            "lambda parameter in a block" => H.Block(H.Code("Func<int, int> twice = (__y) => __y * 2;")),
            "foreach variable" => H.Control(H.Arm("foreach", "(var __item in items)", H.El("li", H.Expr("__item")))),
            _ => H.If("(value is string __s)", H.El("b", H.Expr("__s"))),
        };

        var translation = Run(new HandTree().Content(markup).Code("private int[] items = [1];\nprivate object value = 1;").Tree());

        AssertReservedAtDeclaration(translation, name);
    }

    [Fact]
    public void AReservedMethodNameIsRefusedOnceBesideItsReservedParameter()
    {
        var translation = Run(new HandTree().Code("private int __Twice(int __n)\n{\n    return __n * 2;\n}").Tree());

        Assert.Equal([RazorDiagnostics.ReservedIdentifier, RazorDiagnostics.ReservedIdentifier], translation.Diagnostics.Select(diagnostic => diagnostic.Descriptor));
        Assert.Equal(["__Twice", "__n"], translation.Diagnostics.Select(diagnostic => translation.Original((SourceRange)diagnostic.Location)));
    }

    [Fact]
    public void UnreservedNamesAndTheNamesTheTranslatorSynthesizesAreNotRefused()
    {
        var catalog = Catalog(Component("App.Components.Panel", ("ChildContent", "RenderFragment?")));
        var tree = new HandTree()
            .Content(
                H.Block(H.Code("var _total = 2;")),
                H.Void("input", H.Dir("bind", "name"), H.Dir("ref", "box")),
                H.El("Panel", H.Control(H.Arm("foreach", "(var item_ in items)", H.El("li", H.Expr("item_"))))))
            .Code("private string name = \"\";\nprivate ElementReference box;\nprivate int[] items = [1];\nprivate int Twice(int n_)\n{\n    var total__ = n_ * 2;\n    return total__;\n}")
            .Tree();

        var translation = Run(tree, catalog);

        Assert.Empty(translation.Diagnostics);
        Assert.Contains("RenderTreeBuilder __builder", translation.Text.Contents);
        Assert.Contains("(string __v) =>", translation.Text.Contents);
        Assert.Contains("(ElementReference __r) =>", translation.Text.Contents);
        Assert.Contains("(RenderTreeBuilder __b1) =>", translation.Text.Contents);
    }

    private static void AssertReservedAtDeclaration(Translation translation, string name)
    {
        var diagnostic = Assert.Single(translation.Diagnostics);
        Assert.Same(RazorDiagnostics.ReservedIdentifier, diagnostic.Descriptor);
        var location = (SourceRange)diagnostic.Location;
        Assert.Equal(translation.Tree.Text.Contents.IndexOf(name, StringComparison.Ordinal), location.Offset);
        Assert.Equal(name, translation.Original(location));
        Assert.Null(translation.Unit);
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