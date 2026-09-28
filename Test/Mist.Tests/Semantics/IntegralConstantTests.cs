using System.Globalization;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class IntegralConstantTests
{
    private static readonly IMistType Int32 = Primitive("Int32");
    private static readonly IMistType Int64 = Primitive("Int64");
    private static readonly IMistType UInt32 = Primitive("UInt32");
    private static readonly IMistType UInt64 = Primitive("UInt64");

    [Theory]
    [InlineData((sbyte)-128)]
    [InlineData(byte.MaxValue)]
    [InlineData(short.MinValue)]
    [InlineData(ushort.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(uint.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(ulong.MaxValue)]
    public void EveryIntegralBox_ReadsAsItsOwnValue(object boxed)
    {
        Assert.Equal(
            Convert.ToString(boxed, CultureInfo.InvariantCulture),
            IntegralConstant.Of(boxed)?.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData('c')]
    [InlineData(1.5)]
    [InlineData(1.5f)]
    [InlineData(true)]
    [InlineData("1")]
    [InlineData(null)]
    public void ANonIntegralValue_IsNoIntegralConstant(object? value)
    {
        Assert.Null(IntegralConstant.Of(value));
    }

    [Theory]
    [InlineData("Byte", "255", (byte)255)]
    [InlineData("Byte", "256", null)]
    [InlineData("SByte", "-129", null)]
    [InlineData("UInt64", "18446744073709551615", 18446744073709551615UL)]
    [InlineData("UInt64", "-1", null)]
    [InlineData("Int64", "-9223372036854775808", long.MinValue)]
    [InlineData("Int64", "-9223372036854775809", null)]
    [InlineData("String", "1", null)]
    public void AValueIsBoxedAsItsTypesPrimitive_OnlyWhereItFits(string type, string value, object? expected)
    {
        Assert.Equal(expected, IntegralConstant.Box(Int128.Parse(value, CultureInfo.InvariantCulture), Primitive(type)));
    }

    [Fact]
    public void Arithmetic_FoldsExactly()
    {
        Assert.Equal(2, IntegralConstant.Fold(Binary(Literal(1), BinaryOperatorKind.Add, Literal(1), Int32)));
        Assert.Equal(1_000_000, IntegralConstant.Fold(Binary(Literal(1_000), BinaryOperatorKind.Multiply, Literal(1_000), Int32)));
        Assert.Equal(-6, IntegralConstant.Fold(Negate(Binary(Literal(2), BinaryOperatorKind.Multiply, Literal(3), Int32))));
        Assert.Equal(3, IntegralConstant.Fold(Binary(Literal(7), BinaryOperatorKind.Divide, Literal(2), Int32)));
        Assert.Equal(-1, IntegralConstant.Fold(Binary(Negate(Literal(7)), BinaryOperatorKind.Modulo, Literal(3), Int32)));
    }

    [Fact]
    public void AnOperationThatLeavesItsType_IsNoConstant()
    {
        Assert.Null(IntegralConstant.Fold(Binary(Literal(int.MaxValue), BinaryOperatorKind.Add, Literal(1), Int32)));
        Assert.Null(IntegralConstant.Fold(Binary(Literal(2U, UInt32), BinaryOperatorKind.Subtract, Literal(3U, UInt32), UInt32)));
        Assert.Null(IntegralConstant.Fold(Binary(Literal(ulong.MaxValue, UInt64), BinaryOperatorKind.Multiply, Literal(ulong.MaxValue, UInt64), UInt64)));
        Assert.Null(IntegralConstant.Fold(Binary(Literal(1), BinaryOperatorKind.Divide, Literal(0), Int32)));
    }

    [Fact]
    public void AWideningConversion_KeepsTheValue()
    {
        var widened = new ImplicitConversionExpression(HandBuiltModel.Node(), Literal(int.MinValue), Int64, []);

        Assert.Equal(int.MinValue, IntegralConstant.Fold(widened));
    }

    [Fact]
    public void AStringLiteral_IsNoIntegralConstant()
    {
        Assert.Null(IntegralConstant.Fold(new LiteralExpression(HandBuiltModel.Node(), "1", Primitive("String"))));
    }

    private static ClassType Primitive(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), null, null!);
    }

    private static LiteralExpression Literal(object value, IMistType? type = null)
    {
        return new LiteralExpression(HandBuiltModel.Node(), value, type ?? Int32);
    }

    private static BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right, IMistType type)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, kind, right, type);
    }

    private static UnaryExpression Negate(Expression operand)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, operand, operand.Type);
    }
}