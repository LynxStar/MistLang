// Tier 3 (Component): containment of a constructed CLR generic in the bases its definition declares, over
// the runtime's real metadata. The call that needs it -- `Take(list)` over Take(IEnumerable<int>) and
// Take(string) -- cannot be a probe, whose shell loads no CLR assembly, nor a sample, since Roslyn resolves
// the emitted call again whatever Mist selected. No Roslyn compile and no process spawning.

using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class ClrConstructedContainmentTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly MetadataLoadContext metadataContext;
    private readonly ConstructedType listOfInt;

    public ClrConstructedContainmentTests()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        metadataContext = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));

        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loaded, null, new DiagnosticBag());
        provider.BuildAndRegister(programModel);

        listOfInt = Constructed("System.Collections.Generic.List<>", "Standard.Int32");
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    [Theory]
    [InlineData("System.Collections.Generic.IList<>")]
    [InlineData("System.Collections.Generic.ICollection<>")]
    [InlineData("Standard.IEnumerable<>")]
    public void AConstructedList_IsWithinItsInterfacesOverTheSameArgument(string interfaceName)
    {
        Assert.True(ConversionRelation.IsWithin(listOfInt, Constructed(interfaceName, "Standard.Int32")));
    }

    [Fact]
    public void AConstructedList_IsNotWithinAnInterfaceOverAnotherArgument()
    {
        Assert.False(ConversionRelation.IsWithin(
            listOfInt, Constructed("Standard.IEnumerable<>", "Standard.String")));
    }

    private ConstructedType Constructed(string definitionName, string argumentName)
    {
        return programModel.TypeInterner.GetOrCreateConstructedType(
            programModel.TypesByFQN[definitionName], [programModel.TypesByFQN[argumentName]]);
    }
}