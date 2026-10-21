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