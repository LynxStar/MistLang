using System.Globalization;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// No carrier or witness is spelled from the slot's domain, hence one Int128 integral witness: a
/// long-domain Base closed with 5 and the base of an int-domain Derived closed with 5 are one Mist
/// type, so they must be one C# type.
/// </summary>
internal static class DependentCarrierNames
{
    public static string? Keyword(IMistType domain)
    {
        return DependentDomains.Primitive(domain)?.FullyQualifiedName switch
        {
            "Standard.Int32" => "int",
            "Standard.UInt32" => "uint",
            "Standard.Int64" => "long",
            "Standard.UInt64" => "ulong",
            "Standard.Int16" => "short",
            "Standard.UInt16" => "ushort",
            "Standard.Byte" => "byte",
            "Standard.SByte" => "sbyte",
            "Standard.Boolean" => "bool",
            "Standard.Char" => "char",
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
        if(parameter is PackElementType element && Monomorph.InScope()?.Element(element.Pack, element.Index) is TypeParameter specialized)
        {
            return specialized.Name;
        }

        return parameter.IsDependent && !parameter.IsPack ? $"T{parameter.Name}" : parameter.Name;
    }

    /// <summary>
    /// Reading a dependent parameter as a VALUE, which is the position its C# spelling differs in.
    /// As a type it is the parameter; as a value it is the witness's static member, because what
    /// the parameter is bound to is a carrier struct rather than the value itself.
    /// </summary>
    public static string? ValueRead(IMistType type)
    {
        if(type is not TypeParameter { IsDependent: true, IsPack: false, Domain: { } domain } parameter)
        {
            return null;
        }

        return DependentDomains.Primitive(domain) is { } primitive && DependentDomains.KindOf(primitive) == DependentKind.Integral
            ? $"(({Keyword(domain)}){Parameter(parameter)}.Value)"
            : $"{Parameter(parameter)}.Value";
    }

    public static string? ValueProperty(TypeParameter parameter)
    {
        return parameter is { ValueProperty: not null, Domain: { } domain } && Keyword(domain) is { } keyword && ValueRead(parameter) is { } read
            ? $"public static {keyword} {parameter.Name} => {read};"
            : null;
    }

    public static string? Witness(IMistType domain)
    {
        return DependentDomains.Primitive(domain) is { } primitive ? Witness(DependentDomains.KindOf(primitive)) : null;
    }

    public static string? WitnessType(DependentKind? kind)
    {
        return kind switch
        {
            DependentKind.Integral => "System.Int128",
            DependentKind.Bool => "bool",
            DependentKind.Char => "char",
            _ => null,
        };
    }

    /// <summary>
    /// The struct carrying one value, named so that distinct values never collide: a negative
    /// number takes a <c>Neg</c> prefix because <c>-</c> is not an identifier character, and a char
    /// is spelled in hex ALWAYS rather than only when it is unprintable. The spec's readable form
    /// maps <c>'x'</c> and <c>'X'</c> to one name, and two dependent values sharing a carrier are
    /// two types silently becoming one. Nothing reads this C#, so the trade costs nothing.
    /// </summary>
    public static string? Carrier(object value)
    {
        return DependentDomains.KindOfValue(value) is null ? null : $"Const_{ValuePart(value)}";
    }

    public static string? Carrier(IMistType domain, LiftedType lifted)
    {
        return DependentDomains.Primitive(domain) is { } primitive && DependentDomains.KindOf(primitive) == DependentDomains.KindOfValue(lifted.Value)
            ? Carrier(lifted.Value)
            : null;
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

    public static string? Witness(DependentKind? kind)
    {
        return kind switch
        {
            DependentKind.Integral => "IConstIntegral",
            DependentKind.Bool => "IConstBool",
            DependentKind.Char => "IConstChar",
            _ => null,
        };
    }

    private static string ValuePart(object value)
    {
        switch(value)
        {
            case bool flag:
                return flag ? "True" : "False";

            case char character:
                return $"Char_0x{(int)character:x4}";

            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                return text.StartsWith('-') ? $"Neg{text[1..]}" : text;
        }
    }
}