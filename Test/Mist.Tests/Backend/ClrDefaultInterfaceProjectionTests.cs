using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

// What the real ambient provider reads off a compiled interface with a default member and two interfaces
// restating it (Mist.Tests.InteropFixtures.DefaultInterfaceFixtures).
[Trait("Tier", "2")]
public sealed class ClrDefaultInterfaceProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Fact]
    public void OnlyAnInstanceMemberWithItsOwnBody_CarriesANativeBody()
    {
        var greeter = Interface("IGreeter");

        Assert.Equal(
            ["Bracket:False", "Greet:True", "Name:False"],
            greeter.MethodsList.Select(method => $"{method.Name}:{method.HasNativeBody}").Order(StringComparer.Ordinal));
        Assert.All(greeter.MethodsList, method => Assert.Null(method.Body));
    }

    [Theory]
    [InlineData("IShoutingGreeter", false)]
    [InlineData("IStrictGreeter", true)]
    public void AnInterfacesExplicitRestatement_IsRecordedAgainstTheSlotItRestates(string name, bool reabstracted)
    {
        var (slot, isAbstract) = Assert.Single(Interface(name).NativeSlotOverridesList);

        Assert.Same(Interface("IGreeter").MethodsList.Single(method => method.Name == "Greet"), slot);
        Assert.Equal(reabstracted, isAbstract);
    }

    private AbstractType Interface(string name)
    {
        return (AbstractType)fixture.Model.TypesByFQN[$"Mist.Tests.InteropFixtures.{name}"];
    }
}