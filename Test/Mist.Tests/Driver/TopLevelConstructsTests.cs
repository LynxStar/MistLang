using Mist.Driver;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class TopLevelConstructsTests
{
    private readonly ProgramModelClass model = new();

    [Fact]
    public void CountsTheClassesStructsAndAbstractsAtTheTopOfTheProjectsFiles()
    {
        Unit(model.AddSelfAssembly("Probe"), Class("Point"), Struct("Color"), Abstract("IShape"));

        Assert.Equal(3, TopLevelConstructs.Count(model));
    }

    [Fact]
    public void CountsAReferencedProjectsDeclarationsAsClassicDoes()
    {
        Unit(model.AddSelfAssembly("Probe"), Class("Program"));
        Unit(model.AddReferenceAssembly("Lib", "/lib/mist.toml"), Class("Helper"), Abstract("IHelper"));

        Assert.Equal(3, TopLevelConstructs.Count(model));
    }

    [Fact]
    public void LeavesOutTheStandardLibrary()
    {
        Unit(model.AddStandardAssembly(), Class("List"), Struct("Span"), Abstract("IClonable"));
        Unit(model.AddSelfAssembly("Probe"), Class("Program"));

        Assert.Equal(1, TopLevelConstructs.Count(model));
    }

    [Fact]
    public void CountsADeclarationWithNestedTypesOnce()
    {
        var outer = Class("Outer");
        outer.NestedTypes.Add(new ClassType("Inner", "Probe", outer.Scope, outer, null));
        Unit(model.AddSelfAssembly("Probe"), outer);

        Assert.Equal(1, TopLevelConstructs.Count(model));
    }

    [Fact]
    public void LeavesOutEnumsNewtypesAndAliases()
    {
        Unit(
            model.AddSelfAssembly("Probe"),
            new EnumType("Suit", "Probe", new Scope(null), null, null),
            new Newtype("Meters", "Probe", new Scope(null), null, new NewtypeDeclarationNode()),
            new TypeAlias("Id", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()));

        Assert.Equal(0, TopLevelConstructs.Count(model));
    }

    [Fact]
    public void LeavesOutTheCloneANewtypeIsReplacedBy()
    {
        var source = Class("Tiger");
        var clone = new ClassType("Jaguar", "Probe", new Scope(null), null, null) { CloneSource = source };
        Unit(model.AddSelfAssembly("Probe"), source, clone);

        Assert.Equal(1, TopLevelConstructs.Count(model));
    }

    [Fact]
    public void NoModelHasNoConstructs()
    {
        Assert.Equal(0, TopLevelConstructs.Count(null));
    }

    private static void Unit(MistAssembly assembly, params DeclaredType[] types)
    {
        var unit = new CompilationUnit { SourcePath = $"{assembly.Name}.mist" };
        unit.Types.AddRange(types);
        assembly.CompilationUnits.Add(unit);
    }

    private static ClassType Class(string name)
    {
        return new ClassType(name, "Probe", new Scope(null), null, null);
    }

    private static StructType Struct(string name)
    {
        return new StructType(name, "Probe", new Scope(null), null, null);
    }

    private static AbstractType Abstract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }
}