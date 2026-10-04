using System.Globalization;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel;

/// <summary>
/// What a dependent parameter's domain admits, read through any refinement or alias to the primitive
/// that carries the value. Three kinds never mix -- integral, bool and char -- so a value fills a slot
/// only of its own kind, and a parameter forwards only into a slot whose primitive its own widens to.
/// A char's numeric widening is left out: a char filling an integral slot would need <c>'A'</c> and
/// <c>65</c> to intern as one type argument, and they are two.
/// </summary>
internal static class DependentDomains
{
    public static IMistType? Primitive(IMistType domain)
    {
        var underlying = domain switch
        {
            RefinedType refined => Primitive(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Primitive(aliased),
            _ => domain,
        };

        return underlying is not null && KindOf(underlying) is not null ? underlying : null;
    }

    public static DependentKind? KindOf(IMistType primitive)
    {
        if(IntegralConstant.IsIntegral(primitive))
        {
            return DependentKind.Integral;
        }

        return primitive.FullyQualifiedName switch
        {
            var name when name == WellKnownTypes.Boolean.FullyQualifiedName => DependentKind.Bool,
            var name when name == WellKnownTypes.Char.FullyQualifiedName => DependentKind.Char,
            _ => null,
        };
    }

    public static DependentKind? KindOfValue(object value)
    {
        return value switch
        {
            bool => DependentKind.Bool,
            char => DependentKind.Char,
            _ when IntegralConstant.Of(value) is not null => DependentKind.Integral,
            _ => null,
        };
    }

    public static bool Holds(IMistType domain, object value)
    {
        if(Primitive(domain) is not { } primitive || KindOf(primitive) != KindOfValue(value))
        {
            return false;
        }

        return IntegralConstant.Of(value) is not { } number || IntegralConstant.Fits(number, primitive);
    }

    public static bool Forwards(IMistType from, IMistType to)
    {
        if(Primitive(from) is not { } source || Primitive(to) is not { } target)
        {
            return false;
        }

        return source.FullyQualifiedName == target.FullyQualifiedName
            || (KindOf(source) == DependentKind.Integral
                && KindOf(target) == DependentKind.Integral
                && ConversionRelation.WidensNumerically(source.FullyQualifiedName, target.FullyQualifiedName));
    }

    public static string Spell(object value)
    {
        return value switch
        {
            bool flag => flag ? "true" : "false",
            char character => character is >= ' ' and <= '~' ? character.ToString(CultureInfo.InvariantCulture) : $"\\u{(int)character:x4}",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    public static string TypeOfValue(object value)
    {
        var keyword = value switch
        {
            bool => WellKnownTypes.Boolean.Keyword,
            char => WellKnownTypes.Char.Keyword,
            _ => IntegralConstant.Of(value) switch
            {
                { } number when number >= int.MinValue && number <= int.MaxValue => WellKnownTypes.Int32.Keyword,
                { } number when number >= uint.MinValue && number <= uint.MaxValue => WellKnownTypes.UInt32.Keyword,
                { } number when number >= long.MinValue && number <= long.MaxValue => WellKnownTypes.Int64.Keyword,
                { } => WellKnownTypes.UInt64.Keyword,
                null => null,
            },
        };

        return keyword ?? string.Empty;
    }
}

internal enum DependentKind
{
    Integral,
    Bool,
    Char,
}