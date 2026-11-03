using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class IntegerOperandFoldTests
{
    [Theory]
    [InlineData("Byte", 255L, 1L, 0L)]
    [InlineData("SByte", 127L, 1L, -128L)]
    [InlineData("Int16", 32767L, 1L, -32768L)]
    [InlineData("UInt16", 65535L, 1L, 0L)]
    [InlineData("Int32", 2147483647L, 1L, -2147483648L)]
    [InlineData("UInt32", 4294967295L, 1L, 0L)]
    [InlineData("Int64", long.MaxValue, 1L, long.MinValue)]
    public void FoldingUsesTheSelectedOperationWidth(string name, long left, long right, long expected)
    {
        Assert.Equal((Int128?)expected, IntegerOperandFold.Fold(new HandBuiltModel().Type(name), BinaryOperatorKind.Add, left, right));
    }
    [Theory]
    [InlineData("SByte", -128L, -128L)]
    [InlineData("Int16", -32768L, -32768L)]
    [InlineData("Int32", -2147483648L, -2147483648L)]
    [InlineData("Int64", long.MinValue, long.MinValue)]
    public void NegatingTheMinimumKeepsItsWrappingValue(string name, long value, long expected)
    {
        Assert.Equal((Int128?)expected, IntegerOperandFold.Fold(new HandBuiltModel().Type(name), BinaryOperatorKind.Subtract, 0, value));
    }

    [Theory]
    [InlineData((int)BinaryOperatorKind.Add)]
    [InlineData((int)BinaryOperatorKind.Subtract)]
    [InlineData((int)BinaryOperatorKind.Multiply)]
    [InlineData((int)BinaryOperatorKind.Divide)]
    public void UpperUnsignedFoldingPreservesAllBits(int kind)
    {
        var op = (BinaryOperatorKind)kind;
        var left = op == BinaryOperatorKind.Subtract ? (Int128)0 : op == BinaryOperatorKind.Add ? (Int128)ulong.MaxValue - 1 : ulong.MaxValue;
        var right = op == BinaryOperatorKind.Multiply ? ulong.MaxValue : (Int128)1;
        var expected = op == BinaryOperatorKind.Multiply ? (Int128)1 : ulong.MaxValue;
        Assert.Equal((Int128?)expected, IntegerOperandFold.Fold(new HandBuiltModel().Type("UInt64"), op, left, right));
    }

    [Fact]
    public void UpperUnsignedAdditionStillWrapsToZero()
    {
        Assert.Equal((Int128?)0, IntegerOperandFold.Fold(new HandBuiltModel().Type("UInt64"), BinaryOperatorKind.Add, ulong.MaxValue, 1));
    }
}