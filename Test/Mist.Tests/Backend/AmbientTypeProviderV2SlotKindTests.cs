using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2SlotKindTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Theory]
    [InlineData("RequiredBase", "Key", "Required")]
    [InlineData("DefaultedBase", "Twice", "Defaulted")]
    [InlineData("DefaultedBase", "Hook", "Defaulted")]
    [InlineData("DefaultedBase", "Fixed", "None")]
    [InlineData("SealingDerived", "Twice", "Sealed")]
    [InlineData("OverridingDerived", "Key", "Defaulted")]
    [InlineData("ImplicitImplementer", "Dispose", "None")]
    public void AClassMethod_CarriesTheSlotKindItsClrFlagsSay(string type, string method, string expected)
    {
        Assert.Equal(expected, fixture.Type(type).MethodsList.Single(member => member.Name == method).SlotKind.ToString());
    }

    [Theory]
    [InlineData("RequiredBase", "Size", "Required")]
    [InlineData("DefaultedBase", "Label", "Defaulted")]
    [InlineData("OverridingDerived", "Size", "Defaulted")]
    public void AClassProperty_CarriesTheSlotKindOfItsAccessor(string type, string property, string expected)
    {
        Assert.Equal(expected, fixture.Type(type).PropertiesList.Single(member => member.Name == property).SlotKind.ToString());
    }

    [Fact]
    public void AClassIndexer_CarriesTheSlotKindOfItsAccessor()
    {
        Assert.Equal(NativeSlotKind.Defaulted, Assert.Single(fixture.Type("DefaultedBase").IndexersList).SlotKind);
    }

    [Fact]
    public void AnInterfaceMember_IsNoneWhetherItHasANativeBodyOrNot()
    {
        var greeter = (AbstractType)fixture.Model.TypesByFQN["Mist.Tests.InteropFixtures.IGreeter"];

        Assert.All(greeter.MethodsList, method => Assert.Equal(NativeSlotKind.None, method.SlotKind));
        Assert.Contains(greeter.MethodsList, method => method.HasNativeBody);
    }

    [Theory]
    [InlineData("ToString", 0)]
    [InlineData("Equals", 1)]
    [InlineData("GetHashCode", 0)]
    public void SystemObjectsVirtuals_AreDefaultedLikeAnyProjectedVirtual(string name, int arity)
    {
        using var context = AmbientProviderTestHelper.RuntimeResolvingContext();
        var method = context.CoreAssembly!.GetType(typeof(object).FullName!)!.GetMethods()
            .Single(method => method.Name == name && !method.IsStatic && method.GetParameters().Length == arity);

        Assert.Equal(NativeSlotKind.Defaulted, MethodProjector.SlotKindOf(method));
    }
}