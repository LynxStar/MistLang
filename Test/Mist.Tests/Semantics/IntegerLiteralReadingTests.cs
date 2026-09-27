using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding.Expressions;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class IntegerLiteralReadingTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("2147483647", 2147483647)]
    [InlineData("2147483648", 2147483648U)]
    [InlineData("4294967295", 4294967295U)]
    [InlineData("4294967296", 4294967296L)]
    [InlineData("9223372036854775807", 9223372036854775807L)]
    [InlineData("9223372036854775808", 9223372036854775808UL)]
    [InlineData("18446744073709551615", 18446744073709551615UL)]
    [InlineData("4294967295u", 4294967295U)]
    [InlineData("4294967296U", 4294967296UL)]
    [InlineData("5l", 5L)]
    [InlineData("9223372036854775808L", 9223372036854775808UL)]
    [InlineData("1UL", 1UL)]
    [InlineData("1lu", 1UL)]
    [InlineData("1_000_000", 1000000)]
    [InlineData("0xFF", 255)]
    [InlineData("0x7FFF_FFFF", 2147483647)]
    [InlineData("0x8000_0000", 2147483648U)]
    [InlineData("0x1_0000_0000", 4294967296L)]
    [InlineData("0x8000_0000_0000_0000", 9223372036854775808UL)]
    [InlineData("0xFFFF_FFFF_FFFF_FFFF", 18446744073709551615UL)]
    [InlineData("0XffL", 255L)]
    [InlineData("0xFFu", 255U)]
    [InlineData("0b1010", 10)]
    [InlineData("0B1111_0000L", 240L)]
    [InlineData("0b1u", 1U)]
    public void EachSpelling_ReadsAsTheFirstTypeItsSuffixAllowsThatHoldsIt(string lexeme, object expected)
    {
        var (value, type) = IntegerLiterals.Read(lexeme)!.Value;

        Assert.Equal(expected, value);
        Assert.Equal(expected.GetType().Name, type.Name);
    }

    [Theory]
    [InlineData("18446744073709551616")]
    [InlineData("99999999999999999999UL")]
    [InlineData("0x1_0000_0000_0000_0000")]
    [InlineData("0b1_0000000000000000000000000000000000000000000000000000000000000000")]
    public void AValuePastULongMaxValue_HasNoReading(string lexeme)
    {
        Assert.Null(IntegerLiterals.Read(lexeme));
    }

    [Theory]
    [InlineData("2147483648", int.MinValue)]
    [InlineData("2_147_483_648", int.MinValue)]
    [InlineData("9223372036854775808", long.MinValue)]
    [InlineData("9223372036854775808L", long.MinValue)]
    [InlineData("9223372036854775808l", long.MinValue)]
    public void ANegatedBoundary_ReadsAsTheMinimumOfTheTypeBelow(string lexeme, object expected)
    {
        var (value, type) = IntegerLiterals.ReadNegated(lexeme)!.Value;

        Assert.Equal(expected, value);
        Assert.Equal(expected.GetType().Name, type.Name);
    }

    [Theory]
    [InlineData("2147483648u")]
    [InlineData("2147483648L")]
    [InlineData("9223372036854775808u")]
    [InlineData("9223372036854775808UL")]
    [InlineData("2147483647")]
    [InlineData("5")]
    public void AnyOtherNegatedLiteral_IsNoBoundary(string lexeme)
    {
        Assert.Null(IntegerLiterals.ReadNegated(lexeme));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("1_000", 1000)]
    [InlineData("3u", 3)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("2147483648", null)]
    [InlineData("18446744073709551616", null)]
    public void ALength_IsTheLiteralWhenAnIntHoldsIt(string lexeme, int? expected)
    {
        Assert.Equal(expected, IntegerLiterals.Length(lexeme));
    }

    [Property(Arbitrary = new[] { typeof(IntegerLiteralReadingTests) }, MaxTest = 500)]
    public void AnyValue_InAnySpelling_ReadsBackExactly(IntegerLiteralSpelling spelling)
    {
        var lexeme = Spell(spelling);
        var read = IntegerLiterals.Read(lexeme);

        Assert.True(read is not null, lexeme);
        Assert.Equal(spelling.Value, IntegralConstant.Of(read.Value.Value));
        Assert.Equal(read.Value.Value.GetType().Name, read.Value.Type.Name);
    }

    public static Arbitrary<IntegerLiteralSpelling> Spellings()
    {
        var halves = Gen.Choose(int.MinValue, int.MaxValue);
        var spellings =
            from bits in Gen.Choose(0, 64)
            from high in halves
            from low in halves
            from radix in Gen.Elements(10, 16, 2)
            from suffix in Gen.Elements("", "u", "U", "l", "L", "ul", "UL", "Lu", "lU")
            from separators in Gen.Choose(0, int.MaxValue)
            select new IntegerLiteralSpelling(Masked(((ulong)(uint)high << 32) | (uint)low, bits), radix, suffix, separators);

        return spellings.ToArbitrary();
    }

    private static ulong Masked(ulong value, int bits)
    {
        return bits == 64 ? value : value & ((1UL << bits) - 1);
    }

    private static string Spell(IntegerLiteralSpelling spelling)
    {
        var digits = spelling.Radix switch
        {
            16 => spelling.Value.ToString("X", CultureInfo.InvariantCulture),
            2 => spelling.Value.ToString("B", CultureInfo.InvariantCulture),
            _ => spelling.Value.ToString(CultureInfo.InvariantCulture),
        };
        var separators = new Random(spelling.SeparatorSeed);
        var separated = new StringBuilder().Append(digits[0]);

        foreach(var digit in digits[1..])
        {
            if(separators.Next(4) == 0)
            {
                _ = separated.Append('_');
            }

            _ = separated.Append(digit);
        }

        var prefix = spelling.Radix switch
        {
            16 => "0x",
            2 => "0b",
            _ => "",
        };

        return prefix + separated + spelling.Suffix;
    }
}

public sealed record IntegerLiteralSpelling(ulong Value, int Radix, string Suffix, int SeparatorSeed);