using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class LiftedIntegralDomainIdentityTests
{
    [Fact]
    public void EqualIntegralPayloadsAreInternedSeparatelyByDomain()
    {
        const long payload = 7L;
        var interner = new TypeInterner();
        var intDomain = new StructType("Int32", "Standard", new Scope(null), null, null);
        var byteDomain = new StructType("Byte", "Standard", new Scope(null), null, null);

        var intLift = interner.LiftLiteral(intDomain, payload);
        var byteLift = interner.LiftLiteral(byteDomain, payload);

        Assert.NotSame(intLift, byteLift);
        Assert.Same(intLift, interner.LiftLiteral(intDomain, payload));
        Assert.Same(byteLift, interner.LiftLiteral(byteDomain, payload));
        Assert.Same(intDomain, intLift.Type);
        Assert.Same(byteDomain, byteLift.Type);
        Assert.Equal(payload, Assert.IsType<long>(intLift.Value));
        Assert.Equal(payload, Assert.IsType<long>(byteLift.Value));
    }
}