using System.Numerics;
using Mist.ProgramAnalysis.Refinements;

namespace Mist.ProgramAnalysis.Facts;

internal readonly record struct Rational(BigInteger Numerator, BigInteger Denominator);

internal static class RealNumbers
{
    public static Rational? Of(object? value)
    {
        return value switch
        {
            float number when float.IsFinite(number) => Binary(number),
            double number when double.IsFinite(number) => Binary(number),
            decimal number => Decimal(number),
            _ => null,
        };
    }

    public static BigInteger? Whole(object? value)
    {
        return Of(value) is { Denominator.IsOne: true } whole ? whole.Numerator : null;
    }

    public static Rational Of(RealKind kind, RealPoint point)
    {
        return kind == RealKind.Decimal ? Decimal(point.Decimal) : Binary(point.Binary);
    }

    public static BigInteger Largest(RealKind kind)
    {
        return kind switch
        {
            RealKind.Single => new BigInteger(float.MaxValue),
            RealKind.Double => new BigInteger(double.MaxValue),
            _ => new BigInteger(decimal.MaxValue),
        };
    }

    public static Rational SmallestNormal(RealKind kind)
    {
        return new Rational(BigInteger.One, BigInteger.One << (kind == RealKind.Single ? 126 : 1022));
    }

    public static bool IsPowerOfTwo(BigInteger value)
    {
        return value.Sign > 0 && (value & (value - 1)).IsZero;
    }

    private static Rational Binary(double number)
    {
        var bits = BitConverter.DoubleToInt64Bits(number);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFF;
        if(exponent == 0)
        {
            exponent = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;
        var numerator = number < 0 ? -new BigInteger(mantissa) : new BigInteger(mantissa);
        return exponent >= 0 ? new Rational(numerator << exponent, BigInteger.One) : Reduced(numerator, BigInteger.One << -exponent);
    }

    private static Rational Decimal(decimal number)
    {
        var bits = decimal.GetBits(number);
        var magnitude = new BigInteger((uint)bits[0]) | (new BigInteger((uint)bits[1]) << 32) | (new BigInteger((uint)bits[2]) << 64);
        var scale = (bits[3] >> 16) & 0xFF;
        return Reduced(bits[3] < 0 ? -magnitude : magnitude, BigInteger.Pow(10, scale));
    }

    private static Rational Reduced(BigInteger numerator, BigInteger denominator)
    {
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return divisor.IsZero || divisor.IsOne ? new Rational(numerator, denominator) : new Rational(numerator / divisor, denominator / divisor);
    }
}