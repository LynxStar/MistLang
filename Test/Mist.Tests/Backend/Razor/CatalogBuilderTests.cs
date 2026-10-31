using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "2")]
public sealed class CatalogBuilderTests
{
    private const string Folder = "/probe";
    private const string Components = "Microsoft.AspNetCore.Components";

    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly ComponentScope AppComponents = Scope("App.Components");

    [Fact]
    public void ACodeBlockParameterKeepsItsDeclaredTypeText()
    {
        var catalog = Build(Component("App.Components.Greeting",
            """
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public int Count { get; set; }
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment< Item > Row { get; set; }
            [Parameter] public EventCallback<int> CountChanged { get; set; }
            """));

        var greeting = Resolved(catalog, "Greeting", AppComponents);

        Assert.True(greeting.IsMist);
        Assert.Equal(0, greeting.TypeParameterCount);
        Assert.Equal(
            [
                ("Title", "string", ParameterShape.String),
                ("Count", "int", ParameterShape.Plain),
                ("ChildContent", "RenderFragment?", ParameterShape.RenderFragment),
                ("Row", "RenderFragment< Item >", ParameterShape.TemplatedFragment),
                ("CountChanged", "EventCallback<int>", ParameterShape.EventCallback),
            ],
            Facts(greeting.Parameters));
    }

    [Fact]
    public void OnlyAPropertyCarryingAParameterAttributeIsAParameter()
    {
        var catalog = Build(Component("App.Components.Panel",
            """
            [CascadingParameter] public int Theme { get; set; }
            [Microsoft.AspNetCore.Components.Parameter] public int Qualified { get; set; }
            [ParameterAttribute] public int Suffixed { get; set; }
            [Obsolete] public int Decorated { get; set; }
            public int Bare { get; set; }
            private int stored;
            public int Method() { return stored; }
            """));

        var names = Resolved(catalog, "Panel", AppComponents).Parameters.Select(parameter => parameter.Name);

        Assert.Equal(["Theme", "Qualified", "Suffixed"], names);
    }

    [Fact]
    public void TwoCodeBlocksBothContribute()
    {
        var catalog = Build(Component("App.Components.Split",
            "[Parameter] public int First { get; set; }", "[Parameter] public int Second { get; set; }"));

        Assert.Equal(["First", "Second"], Resolved(catalog, "Split", AppComponents).Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void AnUnparsableCodeBlockContributesNothingAndReportsNothing()
    {
        var diagnostics = new DiagnosticBag();

        var catalog = Build(diagnostics, new ComponentTypeSource(), [],
            [Component("App.Components.Broken", "[Parameter] public int Count { get; set; }", "[Parameter] public int = ;")]);

        Assert.Equal(["Count"], Resolved(catalog, "Broken", AppComponents).Parameters.Select(parameter => parameter.Name));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void EveryComponentClassIsAComponentWhateverItsBase()
    {
        var catalog = Build(new ComponentSource("App.Components.Odd", "Unresolvable", AppComponents, []));

        Assert.Equal("App.Components.Odd", Resolved(catalog, "Odd", AppComponents).FullName);
    }

    [Fact]
    public void AMistClassDerivingAComponentClassIsAComponentTransitivelyAndInheritsItsParameters()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Components/Fancy.mist",
            CatalogAst.Class("Fancier", ast.Base("Fancy"), [ast.Property("Title", "string")]),
            CatalogAst.Class("Fancy", ast.Base("Counter"), [ast.Property("Step", "int")]));

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), [unit],
            [Component("App.Components.Counter", "[Parameter] public int Count { get; set; }\n[Parameter] public int Title { get; set; }")]);

        var fancier = Resolved(catalog, "Fancier", AppComponents);
        Assert.Equal(
            [
                ("Title", "string", ParameterShape.String),
                ("Step", "int", ParameterShape.Plain),
                ("Count", "int", ParameterShape.Plain),
            ],
            Facts(fancier.Parameters));
    }

    [Fact]
    public void AMistClassDerivingAClrComponentThroughAUsingIsAComponentAndInheritsItsParameters()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Layout/MainLayout.mist", [CatalogAst.Class("MainLayout", ast.Base("LayoutComponentBase"))], usings: [Components]);

        var layout = Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []), "MainLayout", Scope("App.Layout"));

        Assert.Equal("App.Layout.MainLayout", layout.FullName);
        Assert.Equal([("Body", $"{Components}.RenderFragment", ParameterShape.RenderFragment)], Facts(layout.Parameters));
    }

    [Fact]
    public void AMistClassImplementingTheComponentInterfaceIsAComponent()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Direct.mist", CatalogAst.Class("Direct", ast.Base($"{Components}.IComponent")));

        Assert.Equal("App.Direct", Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []), "Direct", Scope("App")).FullName);
    }

    [Fact]
    public void AClassWithoutAComponentBaseIsNotAComponent()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Models/Model.mist", [
            CatalogAst.Class("Model", ast.Base("Record"), [ast.Property("Count", "int")]),
            CatalogAst.Class("Record"),
            CatalogAst.Class("Shape", keyword: new AbstractKeywordTypeKeywordNode()),
        ], usings: [Components]);

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []);

        string[] names = ["Model", "Record", "Shape"];
        Assert.All(names, name => Assert.Equal(ResolutionKind.Unknown, catalog.Resolve(name, Scope("App.Models")).Kind));
    }

    [Fact]
    public void ABaseCycleIsNotAComponentAndDoesNotStopTheBuild()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Cycle.mist", CatalogAst.Class("Ping", ast.Base("Pong")), CatalogAst.Class("Pong", ast.Base("Ping")));

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []);

        Assert.Equal(ResolutionKind.Unknown, catalog.Resolve("Ping", Scope("App")).Kind);
    }

    [Fact]
    public void AMistClassIsNamedByItsDeclaredOrInferredNamespace()
    {
        var ast = new CatalogAst();
        var units = ImmutableArray.Create(
            ast.Unit("Widgets/Inferred.mist", CatalogAst.Class("Inferred", ast.Base($"{Components}.ComponentBase"))),
            ast.Unit("Widgets/Declared.mist", [CatalogAst.Class("Declared", ast.Base($"{Components}.ComponentBase"))], declaredNamespace: "Other.Place"),
            ast.BlockUnit("Widgets/Blocks.mist", "Outer", "Inner", CatalogAst.Class("Nested", ast.Base($"{Components}.ComponentBase"))));

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), units, []);

        Assert.Equal("App.Widgets.Inferred", Resolved(catalog, "Inferred", Scope("App.Widgets")).FullName);
        Assert.Equal("Other.Place.Declared", Resolved(catalog, "Declared", Scope("Other.Place")).FullName);
        Assert.Equal("Outer.Inner.Nested", Resolved(catalog, "Nested", Scope("Outer.Inner")).FullName);
    }

    [Fact]
    public void ABaseNameResolvesThroughTheEnclosingNamespacesAndTheGlobalUsings()
    {
        var ast = new CatalogAst();
        var units = ImmutableArray.Create(
            ast.Unit("Shared/Base.mist", CatalogAst.Class("SharedBase", ast.Base("ComponentBase"))),
            ast.Unit("Shared/Deep/Derived.mist", CatalogAst.Class("Derived", ast.Base("SharedBase"))));

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), units, [], [Components]);

        Assert.Equal("App.Shared.Deep.Derived", Resolved(catalog, "Derived", Scope("App.Shared.Deep")).FullName);
    }

    [Fact]
    public void AGenericMistComponentRecordsItsArity()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Grid.mist", CatalogAst.Class("Grid", ast.Base($"{Components}.ComponentBase"), typeParameters: ["TItem", "TKey"]));

        Assert.Equal(2, Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []), "Grid", Scope("App")).TypeParameterCount);
    }

    [Fact]
    public void AnInheritsBaseSuppliesTheComponentClassesInheritedParameters()
    {
        var catalog = Build(new ComponentSource("App.Layout.Shell", "LayoutComponentBase", Scope("App.Layout", Components),
            [Block("[Parameter] public string Body { get; set; } = \"\";\n[Parameter] public int Width { get; set; }")]));

        var shell = Resolved(catalog, "Shell", Scope("App.Layout"));

        Assert.Equal(["Body", "Width"], shell.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(ParameterShape.String, shell.Parameters[0].Shape);
    }

    [Fact]
    public void AClrComponentIsReadLazilyThroughTheAmbientSource()
    {
        var ambient = new ComponentTypeSource();
        var catalog = Build(new DiagnosticBag(), ambient, [], []);
        Assert.DoesNotContain($"{Components}.Routing.Router", ambient.Asked);

        var router = Resolved(catalog, "Router", Scope($"{Components}.Routing"));

        Assert.False(router.IsMist);
        Assert.Equal(
            [
                ("NotFound", $"{Components}.RenderFragment", ParameterShape.RenderFragment),
                ("Found", $"{Components}.RenderFragment<string>", ParameterShape.TemplatedFragment),
                ("Timeout", "int?", ParameterShape.Plain),
            ],
            Facts(router.Parameters));
    }

    [Fact]
    public void AGenericClrComponentIsFoundThroughItsArity()
    {
        var virtualize = Resolved(Build(), "Virtualize", Scope($"{Components}.Web"));

        Assert.Equal(($"{Components}.Web.Virtualize", 1), (virtualize.FullName, virtualize.TypeParameterCount));
    }

    [Fact]
    public void AClrComponentWithFiveTypeParametersIsFoundWithoutAnArityLimit()
    {
        var component = Resolved(Build(), "Five", Scope(Components));

        Assert.Equal(($"{Components}.Five", 5), (component.FullName, component.TypeParameterCount));
    }

    [Theory]
    [InlineData("Outer.Inner", "Vendor", "Vendor.Outer.Inner", 0)]
    [InlineData("Vendor.Outer.Inner", "App", "Vendor.Outer.Inner", 0)]
    [InlineData("Inner", "Vendor.Outer", "Vendor.Outer.Inner", 0)]
    [InlineData("Outer.Middle.Deep", "Vendor", "Vendor.Outer.Middle.Deep", 0)]
    [InlineData("Outer.GenericBase", "Vendor", "Vendor.Outer.GenericBase", 1)]
    public void ANestedClrComponentResolvesThroughItsContainingTypes(string tag, string namespaceName, string fullName, int arity)
    {
        var component = Resolved(Build(), tag, Scope(namespaceName));

        Assert.Equal((fullName, arity, false), (component.FullName, component.TypeParameterCount, component.IsMist));
        Assert.Equal(arity == 0 ? "Title" : "Value", Assert.Single(component.Parameters).Name);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ANestedClrBaseSuppliesParametersToMistAndComponentClasses(bool componentFile, bool generic)
    {
        var ast = new CatalogAst();
        var baseName = generic ? "Outer.GenericBase" : "Outer.Inner";
        var arguments = generic ? new[] { "string" } : [];
        var unit = ast.Unit("Shell.mist", [CatalogAst.Class("Shell", ast.Base(baseName, arguments))], usings: ["Vendor"]);
        var source = new ComponentSource("App.Shell", baseName + (generic ? "<string>" : ""), Scope("App", "Vendor"), []);
        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), componentFile ? [] : [unit], componentFile ? [source] : []);

        Assert.Equal(
            [(generic ? "Value" : "Title", "string", ParameterShape.String)],
            Facts(Resolved(catalog, "Shell", Scope("App")).Parameters));
    }

    [Theory]
    [InlineData("string", nameof(ParameterShape.String))]
    [InlineData("string?", nameof(ParameterShape.String))]
    [InlineData("RenderFragment", nameof(ParameterShape.RenderFragment))]
    [InlineData("RenderFragment<Item>", nameof(ParameterShape.TemplatedFragment))]
    [InlineData("EventCallback<int>", nameof(ParameterShape.EventCallback))]
    [InlineData("int", nameof(ParameterShape.Plain))]
    public void AConstructedMistBaseSubstitutesInheritedParametersBeforeDecidingTheirShape(string argument, string shape)
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Generic.mist",
            CatalogAst.Class("Derived", ast.Base("Middle", argument)),
            CatalogAst.Class("Middle", ast.Base("Base", "U"), typeParameters: ["U"]),
            CatalogAst.Class("Base", ast.Base($"{Components}.ComponentBase"),
                [ast.Property("Value", "T"), ast.Property("Rows", "RenderFragment<T>"), ast.Property("Items", "T[]")], ["T"]));

        var component = Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []), "Derived", Scope("App"));

        Assert.Equal(("Value", argument, Enum.Parse<ParameterShape>(shape)), Facts(component.Parameters).First());
        Assert.Equal($"RenderFragment<{argument}>", component.Parameters[1].TypeText);
        Assert.Equal(argument + "[]", component.Parameters[2].TypeText);
    }

    [Fact]
    public void AnInheritsConstructedMistBaseSubstitutesItsInheritedParameters()
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Generic.mist", CatalogAst.Class("Base", ast.Base($"{Components}.ComponentBase"), [ast.Property("Value", "T")], ["T"]));
        var source = new ComponentSource("App.Shell", "Base<string>", Scope("App"), []);

        var component = Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], [source]), "Shell", Scope("App"));

        Assert.Equal([("Value", "string", ParameterShape.String)], Facts(component.Parameters));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConstructedClrBaseSubstitutesInheritedParameterTypes(bool componentFile)
    {
        var ast = new CatalogAst();
        var unit = ast.Unit("Shell.mist", CatalogAst.Class("Shell", ast.Base($"{Components}.GenericBase", "string")));
        var source = new ComponentSource("App.Shell", "GenericBase<string>", Scope("App", Components), []);
        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), componentFile ? [] : [unit], componentFile ? [source] : []);

        Assert.Equal(
            [("Value", "string", ParameterShape.String), ("Items", "string[]", ParameterShape.Plain)],
            Facts(Resolved(catalog, "Shell", Scope("App")).Parameters));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedMistComponentsResolveWithTheirContainingNamesAndScope(bool structContainer)
    {
        var ast = new CatalogAst();
        var nestedBase = CatalogAst.Class("Base", ast.Base($"{Components}.ComponentBase"), [ast.Property("Title", "string")]);
        var nestedDerived = CatalogAst.Class("Inner", ast.Base("Base"));
        var middle = CatalogAst.Class("Middle", members: [new NestedBodyTypeMemberMemberDeclarationNode { NestedBody = nestedDerived }]);
        var outer = CatalogAst.Class("Outer", members:
        [
            new NestedBodyTypeMemberMemberDeclarationNode { NestedBody = nestedBase },
            new NestedBodyTypeMemberMemberDeclarationNode { NestedBody = middle },
        ], keyword: structContainer ? new StructKeywordTypeKeywordNode() : new ClassKeywordTypeKeywordNode());
        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), [ast.Unit("Nested.mist", outer)], []);

        var component = Resolved(catalog, "Outer.Middle.Inner", Scope("App"));
        Assert.Equal("App.Outer.Middle.Inner", component.FullName);
        Assert.Equal([("Title", "string", ParameterShape.String)], Facts(component.Parameters));
        Assert.Equal(ResolutionKind.Unknown, catalog.Resolve("Outer", Scope("App")).Kind);
    }

    [Fact]
    public void AClrTypeThatIsNotAComponentDoesNotResolve()
    {
        Assert.Equal(ResolutionKind.Unknown, Build().Resolve("MouseEventArgs", Scope($"{Components}.Web")).Kind);
    }

    [Fact]
    public void EventArgumentTypesAreReadFromTheEventHandlersAttributes()
    {
        var catalog = Build();

        Assert.Equal($"{Components}.Web.MouseEventArgs", catalog.EventArgumentType("onclick"));
        Assert.Equal(ComponentCatalog.FallbackEventArgs, catalog.EventArgumentType("onfocus"));
    }

    [Fact]
    public void WithoutEventHandlersEveryEventFallsBack()
    {
        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(eventHandlers: false), [], []);

        Assert.Equal(ComponentCatalog.FallbackEventArgs, catalog.EventArgumentType("onclick"));
    }

    private static ComponentFacts Resolved(ComponentCatalog catalog, string tagName, ComponentScope scope)
    {
        var resolution = catalog.Resolve(tagName, scope);
        Assert.True(resolution.Kind == ResolutionKind.Component, $"'{tagName}' resolved as {resolution.Kind}");
        return resolution.Candidates[0];
    }

    private static IEnumerable<(string Name, string TypeText, ParameterShape Shape)> Facts(ImmutableArray<ParameterFacts> parameters)
    {
        return parameters.Select(parameter => (parameter.Name, parameter.TypeText, parameter.Shape));
    }

    private static ComponentCatalog Build(params ComponentSource[] components)
    {
        return Build(new DiagnosticBag(), new ComponentTypeSource(), [], [.. components]);
    }

    private static ComponentCatalog Build(
        DiagnosticBag diagnostics,
        IAmbientTypeSource ambient,
        ImmutableArray<CompilationUnit> units,
        ImmutableArray<ComponentSource> components,
        ImmutableArray<string> globalUsings = default)
    {
        var project = new ProjectFacts("App", Folder, globalUsings.IsDefault ? [] : globalUsings);
        return CatalogBuilder.Build(new TranslationContext([], units, ambient, Lexer, Parser, project, diagnostics), components);
    }

    private static ComponentSource Component(string fullName, params string[] codeBlocks)
    {
        return new ComponentSource(fullName, null, Scope("App.Components", Components), [.. codeBlocks.Select(Block)]);
    }

    private static CodeBlock Block(string members)
    {
        var span = new SourceSpan(0, members.Length);
        return new CodeBlock(new CodeRegion(members, span), span);
    }

    private static ComponentScope Scope(params string[] namespaces)
    {
        return new ComponentScope([.. namespaces]);
    }
}