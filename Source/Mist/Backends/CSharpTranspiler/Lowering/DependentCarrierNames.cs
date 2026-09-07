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
        // Unwrapped first, because a domain is usually written as a refinement over a primitive
        // rather than as the primitive -- Standard's own Array declares its length as
        // ZeroOrPositiveInt -- and what carries the value is the primitive underneath.
        return Underlying(domain).FullyQualifiedName switch
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

    /// <summary>
    /// The C# name of a type parameter. A dependent one takes a <c>T</c> prefix because it becomes
    /// a C# TYPE parameter while its Mist name is also the static property carrying its value, and
    /// the two would otherwise be one identifier declared twice.
    /// </summary>
    public static string Parameter(TypeParameter parameter)
    {
        return parameter.IsDependent && !parameter.IsPack ? $"T{parameter.Name}" : parameter.Name;
    }

    /// <summary>
    /// Reading a dependent parameter as a VALUE, which is the position its C# spelling differs in.
    /// As a type it is the parameter; as a value it is the witness's static member, because what
    /// the parameter is bound to is a carrier struct rather than the value itself.
    /// </summary>
    public static string? ValueRead(IMistType type)
    {
        return type is TypeParameter { IsDependent: true, IsPack: false } parameter
            ? $"{Parameter(parameter)}.Value"
            : null;
    }

    private static IMistType Underlying(IMistType domain)
    {
        return domain switch
        {
            RefinedType refined => Underlying(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Underlying(aliased),
            _ => domain,
        };
    }

    /// <summary>
    /// Whether a value can inhabit a domain at all. Kinds do not mix: an integral literal lifts as
    /// a long whatever width it fills, but a bool domain admits only a bool and a char only a char,
    /// and pairing them would emit a carrier whose Value does not compile.
    /// </summary>
    public static bool Fits(IMistType domain, object value)
    {
        var keyword = Domain(domain)?.Keyword;

        if(keyword is "bool" or "char" or null)
        {
            return (keyword == "bool" && value is bool) || (keyword == "char" && value is char);
        }

        // Tested for what it IS rather than what it is not: the interner normalizes every integral
        // to a long, and a value that is neither -- a bool, a char, a bound type PACK -- belongs to
        // some other domain entirely rather than to a wider or narrower one of these.
        if(value is not long number)
        {
            return false;
        }

        return keyword switch
        {
            "sbyte" => number is >= sbyte.MinValue and <= sbyte.MaxValue,
            "byte" => number is >= byte.MinValue and <= byte.MaxValue,
            "short" => number is >= short.MinValue and <= short.MaxValue,
            "ushort" => number is >= ushort.MinValue and <= ushort.MaxValue,
            "int" => number is >= int.MinValue and <= int.MaxValue,
            "uint" => number is >= uint.MinValue and <= uint.MaxValue,
            "ulong" => number >= 0,
            _ => true,
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
    public static string? Carrier(IMistType domain, object value)
    {
        return Domain(domain) is { } known ? $"Const_{known.Stem}_{ValuePart(value)}" : null;
    }

    /// <summary>
    /// The carrier for a value standing in a slot of the given domain. The DOMAIN is the slot's,
    /// never the literal's: every integral literal lifts as int, so a 999 filling a
    /// <c>long</c> parameter would otherwise be carried by an IConstInt and refused by the
    /// constraint it was emitted for.
    /// </summary>
    public static string? Carrier(IMistType domain, LiftedType lifted)
    {
        return Carrier(domain, lifted.Value);
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