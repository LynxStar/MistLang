using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class ArithmeticProofTests
{
    private readonly HandBuiltModel built = new();

    private ClassType Integer => Family("Int32", int.MinValue);

    private ClassType Long => Family("Int64", long.MinValue);

    private ClassType Short => Family("Int16", short.MinValue);

    private ClassType Unsigned => Family("UInt32", null);

    private ClassType Family(string name, long? minimum)
    {
        var type = built.Type(name);
        if(type.NestedTypes.Count == 0)
        {
            add("Nonzero", 0);
            if(minimum is { } low)
            {
                add("QuotientDividend", low);
                add("QuotientDivisor", -1);
            }
        }

        return type;

        void add(string contract, long excluded)
        {
            var comparison = new ComparisonPattern(HandBuiltModel.Node(), type, ComparisonKind.Equal, HandBuiltModel.Literal(type, excluded));
            type.NestedTypes.Add(new TypeAlias(contract, "Standard", type.Scope, type, new TypeAliasDeclarationNode())
            {
                AliasedType = new RefinedType(type, HandBuiltModel.Node(), contract)
                {
                    Predicate = new NegatedPattern(HandBuiltModel.Node(), type, comparison),
                },
            });
        }
    }

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
    public void NarrowSignedMinimumIsRefusedAndRemainderIsGuarded()
    {
        Assert.Equal(ArithmeticProof.Verdict.Invalid, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Short, Admitted.Exactly(short.MinValue), Admitted.Exactly(-1)));
        Assert.Equal(ArithmeticProof.Verdict.SafeGuardedRemainder, ArithmeticProof.Judge(
            BinaryOperatorKind.Modulo, Short, Admitted.Exactly(short.MinValue), Admitted.Exactly(-1)));
    }

    [Fact]
    public void UnsignedFamiliesHaveNoOverflowCaseOnlyZero()
    {
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Unsigned, Admitted.Everything, Admitted.AtLeast(1)));
        Assert.Equal(ArithmeticProof.Verdict.Fallible, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, Unsigned, Admitted.Everything, Admitted.Everything));
    }
    [Theory]
    [InlineData((int)BinaryOperatorKind.Divide)]
    [InlineData((int)BinaryOperatorKind.Modulo)]
    public void FullWidthUnsignedConstantsMeetTheDeclaredNonzeroContract(int kind)
    {
        var op = (BinaryOperatorKind)kind;
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(
            op, Family("UInt64", null), Admitted.Everything, Admitted.Everything, rightConstant: ulong.MaxValue));
    }
}