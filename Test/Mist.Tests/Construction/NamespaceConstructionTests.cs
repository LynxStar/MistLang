using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class NamespaceConstructionTests
{
    [Theory]
    [InlineData("A", "B", "A.B")]
    [InlineData("A.B", "C.D", "A.B.C.D")]
    [InlineData("A", "A.B", "A.A.B")]
    public void NestedDeclarationsComposeTheirFullNames(string outer, string inner, string expected)
    {
        var f = new Construction();
        var types = f.Build(Block(outer, Nested(Block(inner, Type("Value"))))).Types;
        var type = Assert.Single(types);

        Assert.Equal(expected, type.NamespaceName);
        Assert.Equal($"{expected}.Value", type.FullyQualifiedName);
        Assert.Equal(new[] { outer, expected }.Order(), f.Model.Namespaces.Keys.Order());
        Assert.Same(type, Assert.IsType<TypeResult>(f.File.Lookup(Request(type.FullyQualifiedName))).Type);
    }

    [Fact]
    public void EmptyNestedBlocksAreRegisteredBeforeTypesAreBuilt()
    {
        var f = new Construction();
        _ = f.Register(Block("A", Nested(Block("B.C", Nested(Block("D"))))));

        Assert.Equal(new[] { "A", "A.B.C", "A.B.C.D" }, f.Model.Namespaces.Keys.Order());
        Assert.Equal("A.B.C", Assert.IsType<NamespaceResult>(f.Model.LookupQualified("A", "B.C")).NamespaceName);
        Assert.Empty(f.Model.TypesByFQN);
    }

    [Fact]
    public void EightLevelsPreserveEveryPrefix()
    {
        var f = new Construction();
        var block = Block("N8", Type("Value"));
        for(var level = 7; level >= 1; level--)
        {
            block = Block($"N{level}", Nested(block));
        }

        var type = Assert.Single(f.Build(block).Types);
        var expected = Enumerable.Range(1, 8).Select(level => string.Join('.', Enumerable.Range(1, level).Select(i => $"N{i}"))).ToArray();

        Assert.Equal(expected, f.Model.Namespaces.Keys.Order());
        Assert.Equal(expected[^1], type.NamespaceName);
        Assert.Same(type, Assert.IsType<TypeResult>(f.Model.LookupQualified(expected[^1], "Value")).Type);
    }

    [Fact]
    public void RepeatedNestedAndQualifiedBlocksShareLookupAcrossFiles()
    {
        var f = new Construction();
        var first = Assert.Single(f.Build(Block("A", Nested(Block("B", Type("First"))))).Types);
        var sharedNamespace = f.Model.Namespaces["A.B"];
        var second = Assert.Single(f.Build(Block("A", Nested(Block("B", Type("Second"))))).Types);
        var third = Assert.Single(f.Build(Block("A.B", Type("Third"))).Types);

        Assert.Same(sharedNamespace, f.Model.Namespaces["A.B"]);
        Assert.Same(second, Assert.IsType<TypeResult>(first.Scope.Lookup(Request("Second"))).Type);
        Assert.Same(first, Assert.IsType<TypeResult>(second.Scope.Lookup(Request("First"))).Type);
        Assert.Same(third, Assert.IsType<TypeResult>(first.Scope.Lookup(Request("Third"))).Type);
        Assert.Equal(3, f.Model.TypesByFQN.Count);
    }

    [Fact]
    public void SiblingNamespacesKeepSameNamedTypesDistinct()
    {
        var f = new Construction();
        var types = f.Build(Block("A", Nested(Block("Left", Type("Value"), Type("LeftOnly"))),
            Nested(Block("Right", Type("Value"))))).Types;
        var left = Assert.Single(types, type => type.FullyQualifiedName == "A.Left.Value");
        var right = Assert.Single(types, type => type.FullyQualifiedName == "A.Right.Value");

        Assert.Same(left, Assert.IsType<TypeResult>(left.Scope.Lookup(Request("Value"))).Type);
        Assert.Same(right, Assert.IsType<TypeResult>(right.Scope.Lookup(Request("Value"))).Type);
        _ = Assert.IsType<NotFoundResult>(right.Scope.Lookup(Request("LeftOnly")));
        _ = Assert.IsType<NotFoundResult>(f.File.Lookup(Request("Value")));
    }

    [Fact]
    public void NestedScopesSeeAncestorsAndShadowTheirTypes()
    {
        var f = new Construction();
        var types = f.Build(Block("A", Type("Ancestor"), Type("Shared"),
            Nested(Block("B", Type("Shared"), Nested(Block("C", Type("Leaf"))))))).Types;
        var ancestor = Assert.Single(types, type => type.Name == "Ancestor");
        var nearest = Assert.Single(types, type => type.FullyQualifiedName == "A.B.Shared");
        var leaf = Assert.Single(types, type => type.Name == "Leaf");

        Assert.Same(ancestor, Assert.IsType<TypeResult>(leaf.Scope.Lookup(Request("Ancestor"))).Type);
        Assert.Same(nearest, Assert.IsType<TypeResult>(leaf.Scope.Lookup(Request("Shared"))).Type);
        Assert.Equal("A.B.C", Assert.IsType<NamespaceResult>(nearest.Scope.Lookup(Request("C"))).NamespaceName);
    }

    [Fact]
    public void ImportsExposeNestedTypesFromAnotherFile()
    {
        var f = new Construction();
        var value = Assert.Single(f.Build(Block("A", Nested(Block("B.C", Type("Value"))))).Types);
        _ = f.Build(new CompilationUnitNode { Usings = [new UsingDeclarationNode { Name = Name("A.B.C") }] });

        Assert.Same(value, Assert.IsType<TypeResult>(f.File.Lookup(Request("Value"))).Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void TopLevelBlocksRemainIndependentOfTheInferredNamespace()
    {
        var f = new Construction();
        var types = f.Build(Block("A.B", Type("First")), Block("A.B", Type("Second")), Block("Other", Type("Third"))).Types;

        Assert.Equal(new[] { "A.B.First", "A.B.Second", "Other.Third" }, types.Select(type => type.FullyQualifiedName));
        Assert.Equal(new[] { "A.B", "Other" }, f.Model.Namespaces.Keys.Order());
        Assert.Same(types[1], Assert.IsType<TypeResult>(types[0].Scope.Lookup(Request("Second"))).Type);
        _ = Assert.IsType<NotFoundResult>(types[0].Scope.Lookup(Request("Third")));
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("with")]
    [InlineData("alias and class")]
    public void AFileWhoseDeclarationsEraseStillDeclaresItsNamespaceForAnEarlierFilesUsing(string declarations)
    {
        var f = new Construction();
        _ = f.Build(Importing("Inferred"));
        _ = f.Build(new CompilationUnitNode { Items = [.. TopLevel(declarations)] });

        Assert.Empty(f.Validate());
        Assert.Equal(["Inferred"], f.Model.Namespaces.Keys);
    }

    [Fact]
    public void AUsingOfANamespaceNoFileDeclaresIsStillRefused()
    {
        var f = new Construction();
        _ = f.Build(Importing("Inferred.Missing"));
        _ = f.Build(new CompilationUnitNode { Items = [.. TopLevel("alias"), .. TopLevel("with")] });

        Assert.Equal("MIST_UNDEFINED_NAMESPACE", Assert.Single(f.Validate()).Number.Value);
    }

    [Fact]
    public void ABlockInAFileScopedNamespaceIsRegisteredWhereItsTypesAreEvenWhenEmpty()
    {
        var f = new Construction();
        var types = f.Build(new CompilationUnitNode
        {
            Items =
            [
                new NamespaceItemTopLevelItemNode
                {
                    NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                    {
                        Name = Name("Outer"),
                        Body = [Nested(Block("Empty")), Nested(Block("Full", Type("Value")))],
                    },
                },
            ],
        }).Types;

        Assert.Equal("Outer.Full.Value", Assert.Single(types).FullyQualifiedName);
        Assert.Equal(new[] { "Outer.Empty", "Outer.Full" }, f.Model.Namespaces.Keys.Order());
    }

    private static CompilationUnitNode Importing(string namespaceName)
    {
        return new CompilationUnitNode { Usings = [new UsingDeclarationNode { Name = Name(namespaceName) }] };
    }

    private static IEnumerable<TopLevelItemNode> TopLevel(string declarations)
    {
        if(declarations.Contains("alias", StringComparison.Ordinal))
        {
            yield return new TypeItemTopLevelItemNode
            {
                TypeDeclaration = new TypeAliasTypeTypeDeclarationNode
                {
                    TypeAliasDeclaration = new TypeAliasDeclarationNode { Name = MistTypeGenerator.MakeToken("Count") },
                },
            };
        }

        if(declarations.Contains("with", StringComparison.Ordinal))
        {
            yield return new WithItemTopLevelItemNode
            {
                WithDeclaration = new WithDeclarationNode
                {
                    TypeName = MistTypeGenerator.MakeToken("Logger"),
                    AbstractName = MistTypeGenerator.MakeToken("INamed"),
                },
            };
        }

        if(declarations.Contains("class", StringComparison.Ordinal))
        {
            yield return new TypeItemTopLevelItemNode { TypeDeclaration = Type("Box").TypeDeclaration };
        }
    }

    private static LookupRequest Request(string name)
    {
        return new LookupRequest(name, MemberVisibility.None, null);
    }

    private static QualifiedIdentifierNode Name(string name)
    {
        var parts = name.Split('.');
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = MistTypeGenerator.MakeToken(parts[0]),
            IDENTIFIERs = parts.Skip(1).Select(part => MistTypeGenerator.MakeToken(part)).ToImmutableArray(),
        };
    }

    private static NamespaceTypeItemNamespaceBodyItemNode Type(string name)
    {
        return new NamespaceTypeItemNamespaceBodyItemNode
        {
            TypeDeclaration = new BodyTypeTypeDeclarationNode
            {
                TypeBody = new TypeBodyDeclarationNode
                {
                    Keyword = new ClassKeywordTypeKeywordNode(),
                    Name = MistTypeGenerator.MakeToken(name),
                    Members = [],
                },
            },
        };
    }

    private static BlockNamespaceNamespaceDeclarationNode Block(string name, params NamespaceBodyItemNode[] body)
    {
        return new BlockNamespaceNamespaceDeclarationNode { Name = Name(name), Body = [.. body] };
    }

    private static NamespaceNamespaceItemNamespaceBodyItemNode Nested(BlockNamespaceNamespaceDeclarationNode block)
    {
        return new NamespaceNamespaceItemNamespaceBodyItemNode { NamespaceDeclaration = block };
    }

    private sealed class Construction
    {
        public Model Model { get; } = new();
        public DiagnosticBag Diagnostics { get; } = new();
        public FileScope File { get; private set; } = null!;
        private readonly BuilderContext context;
        private readonly NamespaceBuilder namespaces;
        private readonly TypeBuilder types;

        public Construction()
        {
            context = new BuilderContext(Model, Diagnostics, NullAmbientTypeProvider.Instance);
            _ = Model.AddSelfAssembly("Test");
            namespaces = new NamespaceBuilder(context);
            types = new TypeBuilder(context);
        }

        public TypeBuilderResult Build(params BlockNamespaceNamespaceDeclarationNode[] blocks)
        {
            return Build(Unit(blocks));
        }

        public TypeBuilderResult Build(CompilationUnitNode ast)
        {
            var fileNamespace = Register(ast);
            var result = types.Build(File, fileNamespace, ast, Model.SelfAssembly!);
            Assert.Empty(Diagnostics.ToImmutableArray());
            return result;
        }

        public string Register(params BlockNamespaceNamespaceDeclarationNode[] blocks)
        {
            return Register(Unit(blocks));
        }

        public ImmutableArray<Diagnostic> Validate()
        {
            UsingValidation.Run(context);
            return Diagnostics.ToImmutableArray();
        }

        private string Register(CompilationUnitNode ast)
        {
            File = new FileScope(context.ScopeFor(Model.SelfAssembly!), Model, Diagnostics);
            return Assert.IsType<string>(namespaces.Build(File, ast, new SourceText("", ""), "Inferred", "/project"));
        }

        private static CompilationUnitNode Unit(BlockNamespaceNamespaceDeclarationNode[] blocks)
        {
            return new CompilationUnitNode
            {
                Items = blocks.Select(block => (TopLevelItemNode)new NamespaceItemTopLevelItemNode { NamespaceDeclaration = block }).ToImmutableArray(),
            };
        }
    }
}