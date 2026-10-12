// Tier 3 (Component): containment of a constructed CLR generic in the bases its definition declares, read
// off compiled metadata (Mist.Tests.InteropFixtures.Bag<T>, declaring the chain List<T> does). The call
// that needs it -- `Take(list)` over Take(IEnumerable<int>) and Take(string) -- cannot be a probe, whose
// shell loads no CLR assembly, nor a sample, since Roslyn resolves the emitted call again whatever Mist
// selected. No Roslyn compile and no process spawning.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class ClrConstructedContainmentTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Theory]
    [InlineData("Mist.Tests.InteropFixtures.IBag<>")]
    [InlineData("Mist.Tests.InteropFixtures.IReadableBag<>")]
    [InlineData("Standard.IEnumerable<>")]
    public void AConstructedBag_IsWithinItsInterfacesOverTheSameArgument(string interfaceName)
    {
        Assert.True(ConversionRelation.IsWithin(BagOfInt, Constructed(interfaceName, "Standard.Int32")));
    }

    [Fact]
    public void AConstructedBag_IsNotWithinAnInterfaceOverAnotherArgument()
    {
        Assert.False(ConversionRelation.IsWithin(
            BagOfInt, Constructed("Standard.IEnumerable<>", "Standard.String")));
    }

    private ConstructedType BagOfInt => Constructed("Mist.Tests.InteropFixtures.Bag<>", "Standard.Int32");

    private ConstructedType Constructed(string definitionName, string argumentName)
    {
        return fixture.Model.TypeInterner.GetOrCreateConstructedType(
            fixture.Model.TypesByFQN[definitionName], [fixture.Model.TypesByFQN[argumentName]]);
    }
}