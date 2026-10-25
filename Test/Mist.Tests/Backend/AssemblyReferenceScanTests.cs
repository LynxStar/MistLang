using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AssemblyReferenceScanTests
{
    [Fact]
    public void ReferencedNamesReadEveryAssemblyReferenceRowInTableOrder()
    {
        using var image = Image("App.Migrations", new Version(1, 0, 0, 0), "App.Data", "System.Runtime");

        Assert.Equal(["App.Data", "System.Runtime"], AssemblyReferenceScan.ReferencedNames(image).AsEnumerable());
    }

    [Fact]
    public void AnAssemblyWithoutReferencesHasNoReferencedNames()
    {
        using var image = Image("Lonely", new Version(1, 0, 0, 0));

        Assert.Empty(AssemblyReferenceScan.ReferencedNames(image));
    }

    [Fact]
    public void AssemblyVersionIsTheAssemblyDefinitionVersion()
    {
        using var image = Image("Newtonsoft.Json", new Version(13, 0, 1, 2), "System.Runtime");

        Assert.Equal(new Version(13, 0, 1, 2), AssemblyReferenceScan.AssemblyVersion(image));
    }

    [Fact]
    public void AModuleWithoutAnAssemblyDefinitionHasNoVersion()
    {
        using var image = Image("Fragment", version: null, "System.Runtime");

        Assert.Null(AssemblyReferenceScan.AssemblyVersion(image));
        Assert.Equal(["System.Runtime"], AssemblyReferenceScan.ReferencedNames(image).AsEnumerable());
    }

    private static PEReader Image(string name, Version? version, params string[] references)
    {
        var metadata = new MetadataBuilder();
        _ = metadata.AddModule(0, metadata.GetOrAddString($"{name}.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        if(version is not null)
        {
            _ = metadata.AddAssembly(metadata.GetOrAddString(name), version, default, default, default, AssemblyHashAlgorithm.Sha1);
        }

        foreach(var reference in references)
        {
            _ = metadata.AddAssemblyReference(
                metadata.GetOrAddString(reference), new Version(4, 2, 0, 0), default, default, default, default);
        }

        _ = metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        _ = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder())
            .Serialize(image);
        return new PEReader(image.ToImmutableArray());
    }
}