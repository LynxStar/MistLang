using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class UnsignedLiteralRefinementFactTests
{
    [Fact]
    public void UInt64MaximumKeepsItsExactMagnitudeInADecimalFact()
    {
        var source = new StructType("UInt64", "Standard", null, null, null);
        var literal = new LiteralExpression(null!, ulong.MaxValue, source);

        Assert.Equal(new LiteralValue(18_446_744_073_709_551_615m, "Standard.Decimal"), AsDecimalFact(literal));
    }

    [Fact]
    public void SignedMinusOneKeepsItsSignInADecimalFact()
    {
        var source = new StructType("Int64", "Standard", null, null, null);
        var literal = new LiteralExpression(null!, -1L, source);

        Assert.Equal(new LiteralValue(-1m, "Standard.Decimal"), AsDecimalFact(literal));
    }

    private static LiteralValue? AsDecimalFact(LiteralExpression literal)
    {
        var target = new StructType("Decimal", "Standard", null, null, null);
        return LiteralValue.Of(literal)?.ConvertedTo(target);
    }
}