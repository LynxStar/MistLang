using System.Globalization;
using Mist.Backends.CSharpTranspiler.Runtime;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class NumericParsingTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData(null, null)]
    [InlineData("invalid", null)]
    [InlineData("2147483648", null)]
    [InlineData("-2147483649", null)]
    public void Int32ParsingReturnsAValueOrAbsence(string? text, int? expected)
    {
        Assert.Equal(expected, NumericParsing.ParseInt32(text));
        Assert.Equal(expected, NumericParsing.ParseInt32(text.AsSpan()));
        Assert.Equal(expected, NumericParsing.ParseInt32(text, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("18446744073709551615", NumberStyles.Integer, ulong.MaxValue)]
    [InlineData("FFFFFFFFFFFFFFFF", NumberStyles.HexNumber, ulong.MaxValue)]
    [InlineData("101010", NumberStyles.BinaryNumber, 42UL)]
    [InlineData("18446744073709551616", NumberStyles.Integer, null)]
    [InlineData("-1", NumberStyles.Integer, null)]
    [InlineData("not-a-number", NumberStyles.Integer, null)]
    [InlineData(null, NumberStyles.Integer, null)]
    public void UInt64ParsingPreservesStylesAndTheUnsignedRange(string? text, NumberStyles style, ulong? expected)
    {
        Assert.Equal(expected, NumericParsing.ParseUInt64(text, style, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RealParsingKeepsTheRequestedProvider()
    {
        var comma = new NumberFormatInfo { NumberDecimalSeparator = "," };
        Assert.Equal(125f, NumericParsing.ParseSingle("1,25e2", NumberStyles.Float, comma));
        Assert.Equal(125d, NumericParsing.ParseDouble("1,25e2", NumberStyles.Float, comma));
        Assert.Equal(125m, NumericParsing.ParseDecimal("1,25e2", NumberStyles.Float, comma));
        Assert.Null(NumericParsing.ParseDouble("1,25e2", NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("1.2.3")]
    public void InvalidRealTextReturnsAbsence(string? text)
    {
        Assert.Null(NumericParsing.ParseSingle(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Null(NumericParsing.ParseDouble(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Null(NumericParsing.ParseDecimal(text, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FloatingSpecialValuesRemainValuesWhileDecimalOverflowIsAbsent()
    {
        Assert.True(float.IsNaN(NumericParsing.ParseSingle("NaN", NumberStyles.Float, CultureInfo.InvariantCulture)!.Value));
        Assert.True(double.IsNaN(NumericParsing.ParseDouble("NaN", NumberStyles.Float, CultureInfo.InvariantCulture)!.Value));
        Assert.Equal(float.PositiveInfinity, NumericParsing.ParseSingle("1e999", NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Equal(double.NegativeInfinity, NumericParsing.ParseDouble("-Infinity", NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.Null(NumericParsing.ParseDecimal("79228162514264337593543950336", NumberStyles.Number, CultureInfo.InvariantCulture));
        Assert.Equal(decimal.MaxValue, NumericParsing.ParseDecimal("79228162514264337593543950335", NumberStyles.Number, CultureInfo.InvariantCulture));
    }
}