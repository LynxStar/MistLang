using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2NullableProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Fact]
    public void NullableValueResult_IsTheMistOptional()
    {
        var result = Assert.IsType<UnionType>(Method(nameof(HasNullableMembers.GetNullableInt)).ReturnType);

        Assert.Equal(["Standard.Int32", "Standard.Void"], result.Constituents.Select(type => type.FullyQualifiedName).Order());
    }

    [Fact]
    public void NullableValueParameter_IsTheSameOptional()
    {
        var parameter = Method(nameof(HasNullableMembers.DoubleOrDefault)).ParametersList.Single();

        _ = Assert.IsType<UnionType>(parameter.Type);
        Assert.Same(Method(nameof(HasNullableMembers.GetNullableInt)).ReturnType, parameter.Type);
    }

    [Fact]
    public void NonNullableValues_AndOutboundReferences_AreNotWrapped()
    {
        Assert.Equal("Standard.Int32", Method(nameof(HasNullableMembers.GetPlainInt)).ReturnType.FullyQualifiedName);
        Assert.IsNotType<UnionType>(Method(nameof(HasNullableMembers.GetPlainStruct)).ReturnType);

        var outbound = Method(nameof(HasNullableMembers.ValueOrFallback)).ParametersList[0];
        Assert.Equal("Standard.String", outbound.Type.FullyQualifiedName);
    }

    [Fact]
    public void ANullableReferenceParameter_IsFlaggedOnTheSlot_WithoutBeingWrapped()
    {
        var slots = (MemberType)fixture.Model.TypesByFQN[$"Mist.Tests.InteropFixtures.{nameof(IHasNullableSlots)}"];
        var slot = slots.MethodsList.Single(method => method.Name == nameof(IHasNullableSlots.Describe));

        Assert.All(slot.ParametersList, parameter => Assert.True(parameter.ClrDeclaresNullable));
        Assert.All(slot.ParametersList, parameter => Assert.IsNotType<UnionType>(parameter.Type));
        Assert.Equal([true, false], Method(nameof(HasNullableMembers.ValueOrFallback)).ParametersList.Select(parameter => parameter.ClrDeclaresNullable));
        Assert.False(Method(nameof(HasNullableMembers.DoubleOrDefault)).ParametersList.Single().ClrDeclaresNullable);
    }

    private Method Method(string name)
    {
        return fixture.Type(nameof(HasNullableMembers)).MethodsList.Single(method => method.Name == name);
    }
}