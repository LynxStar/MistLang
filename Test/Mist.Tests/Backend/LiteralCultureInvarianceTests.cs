// Purity Requirement 13.3: the compiler reads no culture itself. A literal read or written through the current
// culture compiles to a different program on a machine that writes numbers differently, which a run under one
// culture cannot see, so each row runs under German and Swedish: both write the decimal separator as a comma,
// German writes a period between groups, and Swedish writes its minus as U+2212.

using System.Globalization;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class LiteralCultureInvarianceTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("de-DE", "1.5", "1.5", "Double")]
    [InlineData("sv-SE", "1.5", "1.5", "Double")]
    [InlineData("de-DE", "0.25f", "0.25", "Single")]
    [InlineData("sv-SE", "0.25f", "0.25", "Single")]
    [InlineData("de-DE", "2.5m", "2.5", "Decimal")]
    [InlineData("sv-SE", "2.5m", "2.5", "Decimal")]
    [InlineData("sv-SE", "1_000.5d", "1000.5", "Double")]
    public void ARealLiteral_ReadsTheSameUnderAnyCulture(string culture, string lexeme, string expected, string type)
    {
        var (value, wellKnown) = Under(culture, () => LiteralBinder.RealValue(lexeme));

        Assert.NotNull(value);
        Assert.Equal(type, value.GetType().Name);
        Assert.Equal(type, wellKnown.Name);
        Assert.Equal(expected, Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("de-DE", "5_000_000_000L", 5000000000L, "Int64")]
    [InlineData("sv-SE", "18446744073709551615UL", 18446744073709551615UL, "UInt64")]
    [InlineData("sv-SE", "42", 42, "Int32")]
    public void AnIntegerLiteral_ReadsTheSameUnderAnyCulture(string culture, string lexeme, object expected, string type)
    {
        var (value, wellKnown) = Under(culture, () => LiteralBinder.IntegerValue(lexeme));

        Assert.Equal(expected, value);
        Assert.Equal(type, wellKnown.Name);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("de-DE", "0xFF", 255)]
    [InlineData("sv-SE", "0x7FFF_FFFFu", 2147483647U)]
    public void AHexLiteral_ReadsTheSameUnderAnyCulture(string culture, string lexeme, object expected)
    {
        Assert.Equal(expected, Under(culture, () => LiteralBinder.IntegerValue(lexeme)).Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("79228162514264337593543950336.0m")]
    [InlineData("1000000000000000000000000000000000000000.0f")]
    public void ARealLiteralItsTypeCannotHold_HasNoValue(string lexeme)
    {
        Assert.Null(LiteralBinder.RealValue(lexeme).Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("18446744073709551616")]
    [InlineData("18446744073709551616UL")]
    public void AnIntegerLiteralItsTypeCannotHold_HasNoValue(string lexeme)
    {
        Assert.Null(LiteralBinder.IntegerValue(lexeme).Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("de-DE", "Double", "1.5", "1.5")]
    [InlineData("sv-SE", "Double", "-0.5", "-0.5")]
    [InlineData("sv-SE", "Double", "3", "3.0")]
    [InlineData("de-DE", "Single", "0.25", "0.25f")]
    [InlineData("sv-SE", "Single", "-0.25", "-0.25f")]
    [InlineData("de-DE", "Decimal", "1234.5", "1234.5m")]
    [InlineData("sv-SE", "Decimal", "-2.5", "-2.5m")]
    [InlineData("sv-SE", "Int32", "-7", "-7")]
    [InlineData("sv-SE", "Int64", "-5000000000", "-5000000000L")]
    [InlineData("de-DE", "UInt32", "3000000000", "3000000000U")]
    public void ALiteral_IsWrittenTheSameUnderAnyCulture(string culture, string type, string value, string expected)
    {
        object boxed = type switch
        {
            "Double" => double.Parse(value, CultureInfo.InvariantCulture),
            "Single" => float.Parse(value, CultureInfo.InvariantCulture),
            "Decimal" => decimal.Parse(value, CultureInfo.InvariantCulture),
            _ => long.Parse(value, CultureInfo.InvariantCulture),
        };
        var literal = HandBuiltModel.Literal(new HandBuiltModel().Type(type), boxed);

        Assert.Equal(expected, Under(culture, () => BodyEmitter.EmitLiteral(literal)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ALiteralItsTypeCannotHold_IsRefusedWhereItIsBound()
    {
        var fragments = new BindingFragments();

        _ = Assert.IsType<ErrorExpression>(LiteralBinder.BindFloatLiteral(
            BindingFragments.At<FloatLiteralExpressionPrimaryNode>(), "79228162514264337593543950336.0m", fragments.Builder));
        Assert.Equal("MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE", Assert.Single(fragments.Diagnostics.ToImmutableArray()).Id.Value);
    }

    private static T Under<T>(string culture, Func<T> act)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}