using System.Globalization;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Names the C# types that carry a dependent value: one witness interface per domain, and one
/// struct per distinct value, both spelled the same way wherever they are referred to.
/// </summary>
internal static class DependentCarrierNames
{
    /// <summary>
    /// The C# keyword a domain projects to, and the stem its carriers are named from. Null for a
    /// domain no dependent parameter may range over, which construction has already refused.
    /// </summary>
    public static (string Keyword, string Stem)? Domain(IMistType domain)
    {
        return domain.FullyQualifiedName switch
        {
            "Standard.Int32" => ("int", "Int"),
            "Standard.UInt32" => ("uint", "Uint"),
            "Standard.Int64" => ("long", "Long"),
            "Standard.UInt64" => ("ulong", "Ulong"),
            "Standard.Int16" => ("short", "Short"),
            "Standard.UInt16" => ("ushort", "Ushort"),
            "Standard.Byte" => ("byte", "Byte"),
            "Standard.SByte" => ("sbyte", "Sbyte"),
            "Standard.Boolean" => ("bool", "Bool"),
            "Standard.Char" => ("char", "Char"),
            _ => null,
        };
    }

    public static string? Witness(IMistType domain)
    {
        return Domain(domain) is { } known ? $"IConst{known.Stem}" : null;
    }

    /// <summary>
    /// The struct carrying one value, named so that distinct values never collide: a negative
    /// number takes a <c>Neg</c> prefix because <c>-</c> is not an identifier character, and a char
    /// is spelled in hex ALWAYS rather than only when it is unprintable. The spec's readable form
    /// maps <c>'x'</c> and <c>'X'</c> to one name, and two dependent values sharing a carrier are
    /// two types silently becoming one. Nothing reads this C#, so the trade costs nothing.
    /// </summary>
    public static string? Carrier(LiftedType lifted)
    {
        if(Domain(lifted.Type) is not { } known)
        {
            return null;
        }

        return $"Const_{known.Stem}_{ValuePart(lifted.Value)}";
    }

    /// <summary>
    /// The C# literal for a value, which is what the carrier's property returns.
    /// </summary>
    public static string Literal(object value)
    {
        return value switch
        {
            bool flag => flag ? "true" : "false",
            char character => $"'\\u{(int)character:x4}'",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
        };
    }

    private static string ValuePart(object value)
    {
        switch(value)
        {
            case bool flag:
                return flag ? "True" : "False";

            case char character:
                return $"0x{(int)character:x4}";

            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                return text.StartsWith('-') ? $"Neg{text[1..]}" : text;
        }
    }
}