using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UsingSelectionTests
{
    private readonly Model model = new();
    private readonly MistAssembly self;

    public UsingSelectionTests()
    {
        self = model.AddSelfAssembly("App");
    }

    [Fact]
    public void AUsingOfANamespaceWhoseDeclarationsAllEraseIsDropped()
    {
        var declaring = Unit(self);
        declaring.Types.Add(Alias("Count", "Aliases"));
        declaring.Types.Add(Intrinsic("Cell", "Intrinsics"));
        declaring.Types.Add(Variadic("Row<>", "Variadics"));
        _ = model.GetOrAddNamespace("Conformances");

        Assert.Empty(Directives(Unit(self, "Aliases", "Intrinsics", "Variadics", "Conformances")));
    }

    [Fact]
    public void ANamespaceIsKeptWhenOneOfItsDeclarationsIsWritten()
    {
        var declaring = Unit(self);
        declaring.Types.Add(Alias("Label", "Mixed"));
        declaring.Types.Add(Class("Box", "Mixed"));

        Assert.Equal(["Mixed"], Directives(Unit(self, "Mixed")));
    }

    [Theory]
    [InlineData("Parent", true)]
    [InlineData("Parent.Child", true)]
    [InlineData("Parent.Child.Grandchild", false)]
    [InlineData("Par", false)]
    public void AWrittenDeclarationDeclaresItsNamespaceAndEveryEnclosingOne(string imported, bool kept)
    {
        Unit(self).Types.Add(Class("Leaf", "Parent.Child"));
        string[] expected = kept ? [imported] : [];

        Assert.Equal(expected, Directives(Unit(self, imported)));
    }

    [Fact]
    public void ALaterUnitAndAReferencedProjectDeclareNamespacesForAnEarlierUnit()
    {
        var importing = Unit(self, "Library", "Later");
        Unit(self).Types.Add(Class("Widget", "Later"));
        Unit(model.AddReferenceAssembly("Lib", "/lib/mist.toml")).Types.Add(Class("Helper", "Library"));

        Assert.Equal(["Later", "Library"], Directives(importing));
    }

    [Fact]
    public void ClrNamespacesAndNamespacesHoldingOnlyExtensionBlocksAreKept()
    {
        _ = model.RegisterType(new ClassType("Enumerable", "System.Linq", new Scope(null), null, null) { Assembly = MistAssembly.Ambient });
        Unit(self).Extensions.Add(new ExtensionBlock("self", "Extensions", new Scope(null), null));

        Assert.Equal(["Extensions", "System", "System.Linq"], Directives(Unit(self, "System.Linq", "System", "Extensions")));
    }

    [Fact]
    public void StandardIsKeptThoughNoWrittenDeclarationSitsInIt()
    {
        Unit(self).Types.Add(Intrinsic("Int32", "Standard"));

        Assert.Equal(["Standard"], Directives(Unit(self, "Standard")));
    }

    [Fact]
    public void GlobalUsingsAreJudgedLikeTheFilesOwnAndEachIsWrittenOnceInOrdinalOrder()
    {
        var declaring = Unit(self);
        declaring.Types.Add(Class("A", "Alpha"));
        declaring.Types.Add(Class("Z", "Zeta"));
        declaring.Types.Add(Alias("Count", "Aliases"));
        self.GlobalUsings = ["Zeta", "Aliases", "Alpha"];

        Assert.Equal(["Alpha", "Zeta"], Directives(Unit(self, "Zeta", "Aliases")));
    }

    [Fact]
    public void TopLevelStatementsImportOnlyTheNamespacesOfTheirFileThatCSharpHas()
    {
        var script = Unit(self);
        script.Types.Add(Class("Helper", "App"));
        script.Types.Add(Alias("Local", "Script"));
        script.Types.Add(Class("Other", "App"));

        Assert.Equal(["App"], UsingSelection.Of(model).StatementImports(script));
    }

    private IReadOnlyList<string> Directives(CompilationUnit unit)
    {
        return UsingSelection.Of(model).Directives(unit, self);
    }

    private static CompilationUnit Unit(MistAssembly assembly, params string[] usings)
    {
        var unit = new CompilationUnit { SourcePath = $"/{assembly.Name}/{assembly.CompilationUnits.Count}.mist", Usings = [.. usings] };
        assembly.CompilationUnits.Add(unit);
        return unit;
    }

    private static ClassType Class(string name, string namespaceName)
    {
        return new ClassType(name, namespaceName, new Scope(null), null, null);
    }

    private static ClassType Intrinsic(string name, string namespaceName)
    {
        return new ClassType(name, namespaceName, new Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = true },
        };
    }

    private static ClassType Variadic(string name, string namespaceName)
    {
        var type = Class(name, namespaceName);
        type.TypeParametersList.Add(new TypeParameter("Ts", 0, type.Scope, type) { PackKind = TypePackKind.Unbracketed });
        return type;
    }

    private static TypeAlias Alias(string name, string namespaceName)
    {
        return new TypeAlias(name, namespaceName, new Scope(null), null, new TypeAliasDeclarationNode());
    }
}