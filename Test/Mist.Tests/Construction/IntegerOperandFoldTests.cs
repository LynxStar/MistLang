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
        Assert.Equal(expected, IntegerOperandFold.Fold(new HandBuiltModel().Type(name), BinaryOperatorKind.Add, left, right));
    }
}