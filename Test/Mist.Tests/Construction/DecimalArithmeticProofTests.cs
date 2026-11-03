using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class DecimalArithmeticProofTests
{
    private static RealSet Exact(decimal value)
    {
        return RealSet.Exactly(RealKind.Decimal, new LiteralValue(value, "Standard.Decimal"))!;
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Subtract")]
    [InlineData("Multiply")]
    [InlineData("Divide")]
    [InlineData("Modulo")]
    public void ConstantsSelectPlainDecimal(string operation)
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, DecimalArithmeticProof.Judge(Enum.Parse<BinaryOperatorKind>(operation), Exact(4m), Exact(2m)));
    }

    [Fact]
    public void AZeroDivisorIsRefusedEvenWithAnUnknownDividend()
    {
        Assert.Equal(ArithmeticProof.Verdict.Invalid, DecimalArithmeticProof.Judge(
            BinaryOperatorKind.Divide, RealSet.Everything(RealKind.Decimal), Exact(0m)));
    }

    [Fact]
    public void AConstantOverflowIsRefused()
    {
        Assert.Equal(ArithmeticProof.Verdict.Invalid, DecimalArithmeticProof.Judge(
            BinaryOperatorKind.Add, Exact(decimal.MaxValue), Exact(1m)));
    }

    [Fact]
    public void RemainderDoesNotRequireARepresentableQuotient()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, DecimalArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Exact(decimal.MaxValue), Exact(0.1m)));
    }

    [Fact]
    public void AClosedSafeRangeProvesDivision()
    {
        var denominator = RealSet.Compare(RealKind.Decimal, ComparisonKind.GreaterThanOrEqual,
            new LiteralValue(1m, "Standard.Decimal"))!;
        Assert.Equal(ArithmeticProof.Verdict.Safe, DecimalArithmeticProof.Judge(
            BinaryOperatorKind.Divide, RealSet.Everything(RealKind.Decimal), denominator));
    }

    [Fact]
    public void AnOpenRangeApproachingZeroDoesNotProveDivision()
    {
        var denominator = RealSet.Compare(RealKind.Decimal, ComparisonKind.GreaterThan,
            new LiteralValue(0m, "Standard.Decimal"))!;
        Assert.Equal(ArithmeticProof.Verdict.Fallible, DecimalArithmeticProof.Judge(
            BinaryOperatorKind.Divide, RealSet.Everything(RealKind.Decimal), denominator));
    }
}