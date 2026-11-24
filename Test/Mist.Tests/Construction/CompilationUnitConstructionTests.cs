using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using DriverUnit = Mist.Driver.Contracts.CompilationUnit;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class CompilationUnitConstructionTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    [Fact]
    public void EachUnitsTreeAndMetadataDetermineItsSourceNamespaceAndAssembly()
    {
        var model = new Model();
        var self = model.AddSelfAssembly("Application");
        var standard = model.AddStandardAssembly();
        var referencePath = Path.Combine(Root, "Reference", "mist.toml");
        var reference = model.AddReferenceAssembly("Dependency", referencePath);
        var diagnostics = new DiagnosticBag();
        var builder = new CompilationUnitBuilder(new BuilderContext(model, diagnostics, NullAmbientTypeProvider.Instance));
        var inputs = new[]
        {
            (Unit("Reference", "Domain", "ReferencedRoot", "ReferencedType") with { OriginProjectPath = referencePath }, reference),
            (Unit("User", "Services", "UserRoot", "UserType"), self),
            (Unit("Standard", "Bootstrap", "Standard", "StandardType") with { IsStdlib = true }, standard),
        };

        foreach(var (input, assembly) in inputs)
        {
            Assert.NotNull(builder.Build(input));
            var unit = Assert.Single(assembly.CompilationUnits);
            var type = Assert.IsType<ClassType>(Assert.Single(unit.Types));
            var ast = Assert.IsType<CompilationUnitNode>(input.Ast);
            var declaration = Assert.IsType<BodyTypeTypeDeclarationNode>(Assert.Single(ast.GetTypes()));
            var directory = Path.GetFileName(Path.GetDirectoryName(input.Source.Path));

            Assert.Equal(input.Source.Path, unit.SourcePath);
            Assert.Equal(directory, unit.RelativePath);
            Assert.Equal($"{input.NamespaceRoot}.{directory}", type.NamespaceName);
            Assert.Same(declaration, type.DeclarationNode);
            Assert.Same(assembly, type.Assembly);
            Assert.Same(type, model.TypesByFQN[type.FullyQualifiedName]);
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void EmptyNamespaceRootRemainsGlobalInsteadOfUsingTheAssemblyName()
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Application");
        var diagnostics = new DiagnosticBag();
        var builder = new CompilationUnitBuilder(new BuilderContext(model, diagnostics, NullAmbientTypeProvider.Instance));
        var input = Unit("User", "", "", "GlobalType");

        Assert.NotNull(builder.Build(input));

        var unit = Assert.Single(assembly.CompilationUnits);
        Assert.Equal("", unit.RelativePath);
        Assert.Equal("", Assert.Single(unit.Types).NamespaceName);
        Assert.Contains("GlobalType", model.TypesByFQN.Keys);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AUnitRefusedForItsNamespaceShapeContributesNothingLaterStagesCanTripOver()
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Application");
        var diagnostics = new DiagnosticBag();
        var context = new BuilderContext(model, diagnostics, NullAmbientTypeProvider.Instance);
        var ast = new CompilationUnitNode
        {
            Usings = [new UsingDeclarationNode { Name = Name("Missing") }],
            Items =
            [
                Namespace(new BlockNamespaceNamespaceDeclarationNode { Name = Name("First"), Body = [TypeIn("Hidden")] }),
                Namespace(new FileScopedNamespaceNamespaceDeclarationNode { Name = Name("Second"), Body = [TypeIn("Other")] }),
            ],
        };

        Assert.Null(new CompilationUnitBuilder(context).Build(Unit(ast)));
        UsingValidation.Run(context);

        Assert.Equal("MIST0291", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
        Assert.Empty(assembly.CompilationUnits);
        Assert.Empty(model.TypesByFQN);
        Assert.Empty(model.Namespaces);
        Assert.Empty(context.PendingUsings);
    }

    [Fact]
    public void AFileScopedNamespaceInsideABlockIsRefusedWithoutDroppingTheRestOfTheUnit()
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Application");
        var diagnostics = new DiagnosticBag();
        var ast = new CompilationUnitNode
        {
            Items =
            [
                Namespace(new BlockNamespaceNamespaceDeclarationNode
                {
                    Name = Name("Outer"),
                    Body =
                    [
                        TypeIn("Kept"),
                        new NamespaceNamespaceItemNamespaceBodyItemNode
                        {
                            NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode { Name = Name("Inner"), Body = [] },
                        },
                    ],
                }),
            ],
        };

        Assert.NotNull(new CompilationUnitBuilder(new BuilderContext(model, diagnostics, NullAmbientTypeProvider.Instance)).Build(Unit(ast)));

        Assert.Equal("MIST0292", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
        Assert.Equal("Outer.Kept", Assert.Single(Assert.Single(assembly.CompilationUnits).Types).FullyQualifiedName);
    }

    private static DriverUnit Unit(CompilationUnitNode ast)
    {
        var folder = Path.Combine(Root, "User");
        return new DriverUnit(new SourceText(Path.Combine(folder, "Shapes.mist"), ""), "UserRoot", folder) { Ast = ast };
    }

    private static NamespaceItemTopLevelItemNode Namespace(NamespaceDeclarationNode declaration)
    {
        return new NamespaceItemTopLevelItemNode { NamespaceDeclaration = declaration };
    }

    private static NamespaceTypeItemNamespaceBodyItemNode TypeIn(string name)
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

    private static QualifiedIdentifierNode Name(string name)
    {
        return new QualifiedIdentifierNode { IDENTIFIER = MistTypeGenerator.MakeToken(name), IDENTIFIERs = [] };
    }

    private static DriverUnit Unit(string project, string directory, string namespaceRoot, string typeName)
    {
        var folder = Path.Combine(Root, project);
        var declaration = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MistTypeGenerator.MakeToken(typeName),
            Members = [],
        };
        return new DriverUnit(new SourceText(Path.Combine(folder, directory, "Types.mist"), ""), namespaceRoot, folder)
        {
            Ast = CompilationUnitNodeExtensions.CreateCompilationUnit(types:
                [new BodyTypeTypeDeclarationNode { TypeBody = declaration }]),
        };
    }
}