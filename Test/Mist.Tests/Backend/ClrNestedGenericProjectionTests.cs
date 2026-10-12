// Tier 3 (Component): how a CLR nested generic type projects. The CLR repeats a declaring type's
// parameters on the nested type, so Bag`1+Enumerator carries a T that is Bag's, exactly as
// List`1+Enumerator does; projected as an argument of the nested type it spelled `List<int>.Enumerator<int>`,
// and before that left Current typed as an unsubstituted T. Asserted on the projected signature and its
// substitution, over compiled metadata. No Roslyn compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class ClrNestedGenericProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Fact]
    public void TheNestedDefinitionHasNoParametersOfItsOwn()
    {
        var enumerator = Assert.Single(Bag.NestedTypes, nested => nested.Name == "Enumerator");

        Assert.Empty(Assert.IsAssignableFrom<MemberType>(enumerator).TypeParametersList);
    }

    [Fact]
    public void AReturnedNestedTypeCarriesTheEnclosingInstantiationNotAnArgument()
    {
        var returned = Assert.IsType<ConstructedType>(GetEnumerator.ReturnType);

        Assert.Empty(returned.TypeArguments);
        Assert.Same(Bag, Assert.IsType<ConstructedType>(returned.Enclosing).Definition);
    }

    [Fact]
    public void TheReceiverSubstitutesThroughTheEnclosingAndSpellsAsCSharpDoes()
    {
        var int32 = fixture.Model.TypesByFQN["Standard.Int32"];
        var bagOfInt = fixture.Model.TypeInterner.GetOrCreateConstructedType(Bag, [int32]);
        var substitution = TypeSubstitution.ForReceiver(bagOfInt);

        var enumerator = Assert.IsType<ConstructedType>(substitution.Apply(GetEnumerator.ReturnType, fixture.Model.TypeInterner));
        var current = Assert.IsAssignableFrom<MemberType>(enumerator.Definition).PropertiesList.Single(property => property.Name == "Current");

        Assert.Equal("Bag<int>.Enumerator", TypeDisplay.Mist(enumerator));
        Assert.Equal("Mist.Tests.InteropFixtures.Bag<int>.Enumerator", TypeNameEmitter.Emit(enumerator));
        Assert.Same(int32, TypeSubstitution.ForReceiver(enumerator).Apply(current.Type, fixture.Model.TypeInterner));
    }

    private ClassType Bag => fixture.Type("Bag<>");

    private Method GetEnumerator => Bag.MethodsList.Single(method => method.Name == "GetEnumerator" && method.ParametersList.Count == 0);
}