using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class UnsignedDependentValueInterningTests
{
    [Fact]
    public void AdjacentValuesAcrossTheSignedBoundaryHaveDistinctLiftedTypes()
    {
        const ulong firstUnsignedValue = 9_223_372_036_854_775_808UL;
        var domain = new ClassType("Int32", "Standard", new Scope(null), null, null);
        var interner = new TypeInterner();

        var lastShared = interner.LiftLiteral(domain, long.MaxValue);
        var firstUnsigned = interner.LiftLiteral(domain, firstUnsignedValue);

        Assert.NotSame(lastShared, firstUnsigned);
        Assert.Equal(firstUnsignedValue, Assert.IsType<ulong>(firstUnsigned.Value));
    }
}