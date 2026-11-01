using System.Globalization;

namespace Mist.Backends.CSharpTranspiler.Runtime;

internal static class NumericParsing
{
    public static int? ParseInt32(string? text)
    {
        return int.TryParse(text, out var result) ? result : null;
    }

    public static int? ParseInt32(ReadOnlySpan<char> text)
    {
        return int.TryParse(text, out var result) ? result : null;
    }

    public static int? ParseInt32(string? text, IFormatProvider? provider)
    {
        return int.TryParse(text, NumberStyles.Integer, provider, out var result) ? result : null;
    }

    public static ulong? ParseUInt64(string? text, NumberStyles style, IFormatProvider? provider)
    {
        return ulong.TryParse(text, style, provider, out var result) ? result : null;
    }

    public static float? ParseSingle(string? text, NumberStyles style, IFormatProvider? provider)
    {
        return float.TryParse(text, style, provider, out var result) ? result : null;
    }

    public static double? ParseDouble(string? text, NumberStyles style, IFormatProvider? provider)
    {
        return double.TryParse(text, style, provider, out var result) ? result : null;
    }

    public static decimal? ParseDecimal(string? text, NumberStyles style, IFormatProvider? provider)
    {
        return decimal.TryParse(text, style, provider, out var result) ? result : null;
    }
}