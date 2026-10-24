using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class FacadeEmitterTests
{
    private static readonly Version PrimaryVersion = new(1, 2, 0, 0);

    [Fact]
    public void TheFacadeIsNamedAfterTheComposedProjectAndCarriesThePrimaryOutputsVersion()
    {
        using var image = new PEReader(FacadeEmitter.Emit("App.Data", "App", PrimaryVersion, [new("App.Data", "StoreContext")]));
        var reader = image.GetMetadataReader();
        var definition = reader.GetAssemblyDefinition();

        Assert.Equal("App.Data", reader.GetString(definition.Name));
        Assert.Equal(PrimaryVersion, definition.Version);
        Assert.Equal(["<Module>"], reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)));
    }

    [Fact]
    public void TheOnlyReferenceIsThePrimaryOutputAtItsVersion()
    {
        using var image = new PEReader(FacadeEmitter.Emit("App.Data", "App", PrimaryVersion, [new("App.Data", "StoreContext")]));
        var reader = image.GetMetadataReader();

        var reference = Assert.Single(reader.AssemblyReferences);
        Assert.Equal("App", reader.GetString(reader.GetAssemblyReference(reference).Name));
        Assert.Equal(PrimaryVersion, reader.GetAssemblyReference(reference).Version);
    }

    [Fact]
    public void EveryTypeIsOneSortedForwarderToThePrimaryOutput()
    {
        ForwardedType[] types = [new("Standard", "List`1"), new("App.Data", "StoreContext"), new("", "__Void")];

        using var image = new PEReader(FacadeEmitter.Emit("App.Data", "App", PrimaryVersion, types));
        var reader = image.GetMetadataReader();
        EntityHandle primary = Assert.Single(reader.AssemblyReferences);
        var exported = reader.ExportedTypes.Select(reader.GetExportedType).ToList();

        Assert.All(exported, type => Assert.True(type.IsForwarder));
        Assert.All(exported, type => Assert.Equal(primary, type.Implementation));
        Assert.Equal(
            [("", "__Void"), ("App.Data", "StoreContext"), ("Standard", "List`1")],
            exported.Select(type => (reader.GetString(type.Namespace), reader.GetString(type.Name))));
    }

    [Fact]
    public void ANestedTypeIsForwardedThroughItsEnclosersRow()
    {
        ForwardedType[] types = [new("Lib", "Widget/Part"), new("Lib", "Widget")];

        using var image = new PEReader(FacadeEmitter.Emit("Lib", "App", PrimaryVersion, types));
        var reader = image.GetMetadataReader();
        var rows = reader.ExportedTypes.ToList();
        EntityHandle encloserRow = rows[0];
        var encloser = reader.GetExportedType(rows[0]);
        var nested = reader.GetExportedType(rows[1]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Lib", "Widget"), (reader.GetString(encloser.Namespace), reader.GetString(encloser.Name)));
        Assert.Equal(("", "Part"), (reader.GetString(nested.Namespace), reader.GetString(nested.Name)));
        Assert.Equal(encloserRow, nested.Implementation);
    }

    [Fact]
    public void TheImageIsDeterministicWhateverTheInputOrder()
    {
        ForwardedType[] sorted = [new("App.Data", "Order"), new("App.Data", "StoreContext")];
        ForwardedType[] shuffled = [new("App.Data", "StoreContext"), new("App.Data", "Order"), new("App.Data", "StoreContext")];

        Assert.Equal(
            FacadeEmitter.Emit("App.Data", "App", PrimaryVersion, sorted).ToArray(),
            FacadeEmitter.Emit("App.Data", "App", PrimaryVersion, shuffled).ToArray());
    }
}