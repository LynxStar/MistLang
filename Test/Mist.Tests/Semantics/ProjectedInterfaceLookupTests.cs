using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ProjectedInterfaceLookupTests
{
    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void LookupNeverEntersAProjectedInterfaceFromAProjectedConcreteType(bool projectedClass, bool projectedInterface, bool searched)
    {
        var owner = new ClassType("Dictionary<,>", "Probe", new Scope(null), null, null);
        var contract = new AbstractType("IDictionary", "Probe", new Scope(null), null, null);
        owner.Assembly = projectedClass ? MistAssembly.Ambient : MistAssembly.Synthetic;
        contract.Assembly = projectedInterface ? MistAssembly.Ambient : MistAssembly.Synthetic;

        Assert.Equal(searched, TypeMemberLookup.Searches(owner, contract));
        Assert.Equal(searched, TypeMemberLookup.Searches(owner, built.Interner.GetOrCreateConstructedType(contract, [built.Type("Int32")])));
    }

    [Fact]
    public void AProjectedInterfaceIsSearchedFromAnotherInterface()
    {
        var derived = new AbstractType("IList", "Probe", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        var inherited = new AbstractType("ICollection", "Probe", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };

        Assert.True(TypeMemberLookup.Searches(derived, inherited));
    }
}