using System.Globalization;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Reads a decimal, hex or binary integer literal into its exact value and the type the binary-literals spec
/// gives it: by suffix (Requirement 3), separators ignored (Requirement 8.10). Where the suffix's type cannot
/// hold the value the spec is silent, and the next type that can is taken, as C# does -- classic emits the
/// literal as written, so C#'s reading is the behavior it has always had.
/// </summary>
internal static class IntegerLiterals
{
    public static (object Value, WellKnownType Type)? Read(string lexeme)
    {
        if(Parse(lexeme) is not (var magnitude, var unsigned, var wide))
        {
            return null;
        }

        return (unsigned, wide) switch
        {
            (false, false) when magnitude <= int.MaxValue => ((int)magnitude, WellKnownTypes.Int32),
            (_, false) when magnitude <= uint.MaxValue => ((uint)magnitude, WellKnownTypes.UInt32),
            (false, _) when magnitude <= long.MaxValue => ((long)magnitude, WellKnownTypes.Int64),
            _ => (magnitude, WellKnownTypes.UInt64),
        };
    }

    public static (object Value, WellKnownType Type)? ReadNegated(string lexeme)
    {
        return Parse(lexeme) switch
        {
            (2147483648, false, false) => (int.MinValue, WellKnownTypes.Int32),
            (9223372036854775808, false, _) => (long.MinValue, WellKnownTypes.Int64),
            _ => null,
        };
    }

    public static int? Length(string lexeme)
    {
        return IntegralConstant.Of(Read(lexeme)?.Value) is { } length && length <= int.MaxValue ? (int)length : null;
    }

    private static (ulong Magnitude, bool Unsigned, bool Wide)? Parse(string lexeme)
    {
        var style = Radix(lexeme);
        var digits = style == NumberStyles.None ? lexeme : lexeme[2..];
        var suffixAt = digits.IndexOfAny(['u', 'U', 'l', 'L']);
        var suffix = suffixAt < 0 ? "" : digits[suffixAt..];
        var text = (suffixAt < 0 ? digits : digits[..suffixAt]).Replace("_", "", StringComparison.Ordinal);

        return ulong.TryParse(text, style, CultureInfo.InvariantCulture, out var magnitude)
            ? (magnitude, suffix.Contains('u', StringComparison.OrdinalIgnoreCase), suffix.Contains('l', StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static NumberStyles Radix(string lexeme)
    {
        if(lexeme.Length < 3 || lexeme[0] != '0')
        {
            return NumberStyles.None;
        }

        return lexeme[1] switch
        {
            'x' or 'X' => NumberStyles.AllowHexSpecifier,
            'b' or 'B' => NumberStyles.AllowBinarySpecifier,
            _ => NumberStyles.None,
        };
    }
}