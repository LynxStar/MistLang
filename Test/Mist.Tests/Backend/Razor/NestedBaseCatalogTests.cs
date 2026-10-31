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
public sealed class NestedBaseCatalogTests
{
    private const string Components = "Microsoft.AspNetCore.Components";

    public static TheoryData<bool, int, string, bool, bool> Rows
    {
        get
        {
            var rows = new TheoryData<bool, int, string, bool, bool>();
            foreach(var clr in new[] { false, true })
            {
                foreach(var depth in new[] { 1, 2 })
                {
                    foreach(var mode in NestedBaseMatrix.Modes)
                    {
                        foreach(var componentFile in new[] { false, true })
                        {
                            foreach(var genericInner in new[] { false, true })
                            {
                                rows.Add(clr, depth, mode, componentFile, genericInner);
                            }
                        }
                    }
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryNestedBaseShapeFlowsThroughBothWrapperForms(bool clr, int depth, string mode, bool componentFile, bool genericInner)
    {
        var ast = new CatalogAst();
        var generic = mode != "NonGeneric";
        var argument = NestedBaseMatrix.Argument(mode);
        var baseText = NestedBaseMatrix.BaseText(depth, mode, genericInner);
        var members = ParameterMembers(ast, generic ? "T" : "string");
        if(genericInner)
        {
            members = members.Add(ast.Property("Other", "V"));
        }

        if(generic && depth == 2)
        {
            members = members.Add(ast.Property("MiddleValue", "U"));
        }

        var inner = CatalogAst.Class(genericInner ? "GenericInner" : "Inner", ast.Base($"{Components}.ComponentBase"), members, genericInner ? ["V"] : []);
        var container = depth == 1 ? inner
            : CatalogAst.Class("Middle", members: [Nested(inner)], typeParameters: generic ? ["U"] : []);
        var outer = CatalogAst.Class(generic ? "MatrixBox" : "MatrixOuter", members: [Nested(container)], typeParameters: generic ? ["T"] : []);
        var shell = CatalogAst.Class("Shell", ast.BaseText(baseText), typeParameters: mode == "Open" ? (depth == 1 ? ["T", "V"] : ["T", "U", "V"]) : []);
        var declarations = new List<TypeBodyDeclarationNode>();
        if(!clr)
        {
            declarations.Add(outer);
        }

        if(!componentFile)
        {
            declarations.Add(shell);
        }

        var unit = ast.Unit("Matrix.mist", [.. declarations], usings: ["Vendor"]);
        var catalog = Build([unit], componentFile ? baseText : "Shell");
        var parameters = Resolved(catalog, "Page").Parameters;

        AssertParameters(parameters, argument, clr, generic);
        if(genericInner)
        {
            Assert.Contains(new ParameterFacts("Other", mode == "Open" ? "V" : "bool", ParameterShape.Plain), parameters);
        }

        if(generic && depth == 2)
        {
            Assert.Contains(new ParameterFacts("MiddleValue", mode == "Open" ? "U" : "int", ParameterShape.Plain), parameters);
        }

        Assert.Equal(6 + (generic && depth == 2 ? 1 : 0) + (genericInner ? 1 : 0), parameters.Length);
        var definition = Resolved(catalog, (clr ? "Vendor." : "") + (generic ? "MatrixBox" : "MatrixOuter") + (depth == 2 ? ".Middle" : "") + (genericInner ? ".GenericInner" : ".Inner"));
        Assert.Equal((generic ? depth : 0) + (genericInner ? 1 : 0), definition.TypeParameterCount);
        Assert.Equal(!clr, definition.IsMist);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    public void ASiblingBaseKeepsImplicitEnclosingArguments(int depth, bool qualified, bool componentFile)
    {
        var ast = new CatalogAst();
        var members = ParameterMembers(ast, "T");
        members = members.Add(ast.Property("Other", "W"));
        if(depth == 2)
        {
            members = members.Add(ast.Property("MiddleValue", "U"));
        }

        var baseClass = CatalogAst.Class("Base", ast.Base($"{Components}.ComponentBase"), members, ["W"]);
        var prefix = depth == 1 ? "Box<T>." : "Box<T>.Middle<U>.";
        var inner = CatalogAst.Class("Inner", ast.BaseText((qualified ? prefix : "") + "Base<V>"), typeParameters: ["V"]);
        var container = depth == 1
            ? CatalogAst.Class("Box", members: [Nested(baseClass), Nested(inner)], typeParameters: ["T"])
            : CatalogAst.Class("Box", members: [Nested(CatalogAst.Class("Middle", members: [Nested(baseClass), Nested(inner)], typeParameters: ["U"]))], typeParameters: ["T"]);
        var baseText = depth == 1 ? "Box<string>.Inner<bool>" : "Box<string>.Middle<int>.Inner<bool>";
        var shell = CatalogAst.Class("Shell", ast.BaseText(baseText));
        var unit = componentFile ? ast.Unit("Box.mist", container) : ast.Unit("Box.mist", container, shell);

        var catalog = Build([unit], componentFile ? baseText : "Shell");
        var parameters = Resolved(catalog, "Page").Parameters;

        AssertParameters(parameters, "string", clr: false, generic: true);
        Assert.Contains(ParameterFacts.Declared("Other", "bool"), parameters);
        if(depth == 2)
        {
            Assert.Contains(ParameterFacts.Declared("MiddleValue", "int"), parameters);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ANestedBaseIsInheritedFromTheEnclosingTypesBase(bool clr, bool componentFile)
    {
        var ast = new CatalogAst();
        var nested = CatalogAst.Class("Base", ast.Base($"{Components}.ComponentBase"), ParameterMembers(ast, "T"));
        var box = CatalogAst.Class("Box", members: [Nested(nested)], typeParameters: ["T"]);
        var derived = CatalogAst.Class("D", ast.BaseText(clr ? "Inner" : "Base"));
        var sub = CatalogAst.Class("Sub", ast.BaseText(clr ? "Vendor.MatrixBox<S>" : "Box<S>"), [Nested(derived)], ["S"]);
        var shell = CatalogAst.Class("Shell", ast.BaseText("Sub<string>.D"));
        var declarations = new List<TypeBodyDeclarationNode> { sub };
        if(!clr)
        {
            declarations.Add(box);
        }

        if(!componentFile)
        {
            declarations.Add(shell);
        }

        var catalog = Build([ast.Unit("Inherited.mist", [.. declarations])], componentFile ? "Sub<string>.D" : "Shell");

        AssertParameters(Resolved(catalog, "Page").Parameters, "string", clr, generic: true);
        Assert.Equal(1, Resolved(catalog, "Sub.D").TypeParameterCount);
    }

    private static ImmutableArray<MemberDeclarationNode> ParameterMembers(CatalogAst ast, string argument)
    {
        return
        [
            ast.Property("Value", argument),
            ast.Property("Optional", argument + "?"),
            ast.Property("Items", argument + "[]"),
            ast.Property("Values", "List<" + argument + ">"),
            ast.Property("Template", "RenderFragment<" + argument + ">"),
        ];
    }

    private static NestedBodyTypeMemberMemberDeclarationNode Nested(TypeBodyDeclarationNode body)
    {
        return new NestedBodyTypeMemberMemberDeclarationNode { NestedBody = body };
    }

    private static ComponentCatalog Build(ImmutableArray<CompilationUnit> units, string baseText)
    {
        var body = "[Parameter] public int Local { get; set; }";
        var span = new SourceSpan(0, body.Length);
        var source = new ComponentSource("App.Page", baseText, new ComponentScope(["App", "Vendor"]),
            [new CodeBlock(new CodeRegion(body, span), span)]);
        return CatalogBuilder.Build(new TranslationContext([], units, new ComponentTypeSource(), new MistLexer(), new MistParser(),
            new ProjectFacts("App", "/probe", []), new DiagnosticBag()), [source]);
    }

    private static ComponentFacts Resolved(ComponentCatalog catalog, string name)
    {
        var resolution = catalog.Resolve(name, new ComponentScope(["App"]));
        Assert.Equal(ResolutionKind.Component, resolution.Kind);
        return Assert.Single(resolution.Candidates);
    }

    private static void AssertParameters(ImmutableArray<ParameterFacts> parameters, string argument, bool clr, bool generic)
    {
        var scalar = argument == "string" ? ParameterShape.String : ParameterShape.Plain;
        var optionalType = clr && !generic ? "int?" : argument + "?";
        var optionalShape = clr && !generic ? ParameterShape.Plain : scalar;
        Assert.Contains(new ParameterFacts("Local", "int", ParameterShape.Plain), parameters);
        Assert.Contains(new ParameterFacts("Value", argument, scalar), parameters);
        Assert.Contains(new ParameterFacts("Optional", optionalType, optionalShape), parameters);
        Assert.Contains(new ParameterFacts("Items", argument + "[]", ParameterShape.Plain), parameters);
        Assert.Contains(new ParameterFacts("Values", (clr ? "System.Collections.Generic.List<" : "List<") + argument + ">", ParameterShape.Plain), parameters);
        Assert.Contains(new ParameterFacts("Template", (clr ? Components + ".RenderFragment<" : "RenderFragment<") + argument + ">", ParameterShape.TemplatedFragment), parameters);
    }
}