using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class ArithmeticProofTests
{
    private readonly HandBuiltModel built = new();

    private ClassType Integer => built.Type("Int32");

    private ClassType Long => built.Type("Int64");

    private ClassType Short => built.Type("Int16");

    private ClassType Unsigned => built.Type("UInt32");

    [Fact]
    public void UnknownDivisorIsFallible()
    {
        Assert.Equal(ArithmeticProof.Verdict.Fallible, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.Everything, Admitted.Everything));
    }

    [Fact]
    public void PositiveDivisorExcludesBothHazards()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.Everything, Admitted.AtLeast(1)));
    }

    [Fact]
    public void ConstantOperandsWithValidQuotientAreSafe()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.Exactly(17), Admitted.Exactly(5)));
    }

    [Fact]
    public void ZeroDivisorIsRefused()
    {
        Assert.Equal(ArithmeticProof.Verdict.Invalid, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.Everything, Admitted.Exactly(0)));
    }

    [Fact]
    public void NonzeroDivisorAloneLeavesOverflowPossible()
    {
        Assert.Equal(ArithmeticProof.Verdict.Fallible, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.Everything, Admitted.Exactly(-1)));
    }

    [Fact]
    public void NegativeOneDivisorIsSafeWhenDividendExcludesMinimum()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.AtLeast(int.MinValue + 1), Admitted.Exactly(-1)));
    }

    [Fact]
    public void MinimumDividedByNegativeOneIsRefused()
    {
        Assert.Equal(ArithmeticProof.Verdict.Invalid, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Integer, Admitted.Exactly(int.MinValue), Admitted.Exactly(-1)));
    }

    [Fact]
    public void RemainderMinimumByNegativeOneIsGuardedNotBare()
    {
        Assert.Equal(ArithmeticProof.Verdict.SafeGuardedRemainder, ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Integer, Admitted.Exactly(int.MinValue), Admitted.Exactly(-1)));
    }

    [Fact]
    public void RemainderIsBareWhenMinimumExcluded()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Integer, Admitted.AtLeast(int.MinValue + 1), Admitted.Exactly(-1)));
    }

    [Fact]
    public void RemainderIsBareWhenDivisorExcludesNegativeOne()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Integer, Admitted.Exactly(int.MinValue), Admitted.AtLeast(1)));
    }

    [Fact]
    public void RemainderByZeroIsRefused()
    {
        Assert.Equal(ArithmeticProof.Verdict.Invalid, ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Integer, Admitted.Everything, Admitted.Exactly(0)));
    }

    [Fact]
    public void AdditionIsNotJudged()
    {
        Assert.Null(ArithmeticProof.Judge(
            BinaryOperatorKind.Add, Integer, Admitted.Everything, Admitted.Exactly(0)));
    }

    [Fact]
    public void WiderSignedFamiliesAreJudgedWithTheirOwnMinimum()
    {
        Assert.Equal(ArithmeticProof.Verdict.Fallible, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Long, Admitted.Everything, Admitted.Everything));
        Assert.Equal(ArithmeticProof.Verdict.Invalid, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Long, Admitted.Exactly(long.MinValue), Admitted.Exactly(-1)));
        Assert.Equal(ArithmeticProof.Verdict.SafeGuardedRemainder, ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Long, Admitted.Exactly(long.MinValue), Admitted.Exactly(-1)));
    }

    [Fact]
    public void NarrowSignedFamiliesPromoteAndAreNotJudgedAtTheirOwnWidth()
    {
        // Int16/SByte drop their division/remainder operators and promote into Int32, so the proof is
        // never asked about the narrow width -- its unrepresentable minimum / -1 cannot be a narrow error.
        Assert.Null(ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Short, Admitted.Exactly(short.MinValue), Admitted.Exactly(-1)));
        Assert.Null(ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Short, Admitted.Everything, Admitted.Everything));
    }

    [Fact]
    public void UnsignedFamiliesHaveNoOverflowCaseOnlyZero()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Unsigned, Admitted.Everything, Admitted.AtLeast(1)));
        Assert.Equal(ArithmeticProof.Verdict.Fallible, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Unsigned, Admitted.Everything, Admitted.Everything));
    }
}