using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;

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
        var unit = Unit("Components/Fancy.mist",
            """
            public class Fancier : Fancy
            {
                [Parameter] public string Title { get; set; } = "";
            }

            public class Fancy : Counter
            {
                [Parameter] public int Step { get; set; }
            }
            """);

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
        var unit = Unit("Layout/MainLayout.mist",
            $$"""
            using {{Components}};

            public class MainLayout : LayoutComponentBase
            {
            }
            """);

        var layout = Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []), "MainLayout", Scope("App.Layout"));

        Assert.Equal("App.Layout.MainLayout", layout.FullName);
        Assert.Equal([("Body", $"{Components}.RenderFragment", ParameterShape.RenderFragment)], Facts(layout.Parameters));
    }

    [Fact]
    public void AMistClassImplementingTheComponentInterfaceIsAComponent()
    {
        var unit = Unit("Direct.mist", $"public class Direct : {Components}.IComponent\n{{\n}}\n");

        Assert.Equal("App.Direct", Resolved(Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []), "Direct", Scope("App")).FullName);
    }

    [Fact]
    public void AClassWithoutAComponentBaseIsNotAComponent()
    {
        var unit = Unit("Models/Model.mist",
            $$"""
            using {{Components}};

            public class Model : Record
            {
                [Parameter] public int Count { get; set; }
            }

            public class Record
            {
            }

            public abstract Shape
            {
            }
            """);

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []);

        string[] names = ["Model", "Record", "Shape"];
        Assert.All(names, name => Assert.Equal(ResolutionKind.Unknown, catalog.Resolve(name, Scope("App.Models")).Kind));
    }

    [Fact]
    public void ABaseCycleIsNotAComponentAndDoesNotStopTheBuild()
    {
        var unit = Unit("Cycle.mist", "public class Ping : Pong\n{\n}\n\npublic class Pong : Ping\n{\n}\n");

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), [unit], []);

        Assert.Equal(ResolutionKind.Unknown, catalog.Resolve("Ping", Scope("App")).Kind);
    }

    [Fact]
    public void AMistClassIsNamedByItsDeclaredOrInferredNamespace()
    {
        var units = ImmutableArray.Create(
            Unit("Widgets/Inferred.mist", $"public class Inferred : {Components}.ComponentBase\n{{\n}}\n"),
            Unit("Widgets/Declared.mist", $"namespace Other.Place;\n\npublic class Declared : {Components}.ComponentBase\n{{\n}}\n"),
            Unit("Widgets/Blocks.mist", $"namespace Outer\n{{\n    namespace Inner\n    {{\n        public class Nested : {Components}.ComponentBase\n        {{\n        }}\n    }}\n}}\n"));

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), units, []);

        Assert.Equal("App.Widgets.Inferred", Resolved(catalog, "Inferred", Scope("App.Widgets")).FullName);
        Assert.Equal("Other.Place.Declared", Resolved(catalog, "Declared", Scope("Other.Place")).FullName);
        Assert.Equal("Outer.Inner.Nested", Resolved(catalog, "Nested", Scope("Outer.Inner")).FullName);
    }

    [Fact]
    public void ABaseNameResolvesThroughTheEnclosingNamespacesAndTheGlobalUsings()
    {
        var units = ImmutableArray.Create(
            Unit("Shared/Base.mist", "public class SharedBase : ComponentBase\n{\n}\n"),
            Unit("Shared/Deep/Derived.mist", "public class Derived : SharedBase\n{\n}\n"));

        var catalog = Build(new DiagnosticBag(), new ComponentTypeSource(), units, [], [Components]);

        Assert.Equal("App.Shared.Deep.Derived", Resolved(catalog, "Derived", Scope("App.Shared.Deep")).FullName);
    }

    [Fact]
    public void AGenericMistComponentRecordsItsArity()
    {
        var unit = Unit("Grid.mist", $"public class Grid<TItem, TKey> : {Components}.ComponentBase\n{{\n}}\n");

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

    private static CompilationUnit Unit(string relativePath, string contents)
    {
        var source = new SourceText(Path.Combine(Folder, relativePath), contents);
        var diagnostics = new DiagnosticBag();
        var parsed = Parser.Parse(Lexer.Lex(source, diagnostics), diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Empty(parsed.Diagnostics);
        return new CompilationUnit(source, "App", Folder) { Ast = parsed.Root };
    }

    private static ComponentScope Scope(params string[] namespaces)
    {
        return new ComponentScope([.. namespaces]);
    }
}