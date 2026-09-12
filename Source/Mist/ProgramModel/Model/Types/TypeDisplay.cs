using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Renders a type as Mist source spells it, for diagnostics.
/// <para>
/// Separate from <see cref="IMistType.Name"/>, which is a structural name built for identity and
/// debugging: it reports a primitive by its Standard name and carries a generic definition's arity
/// marker, so a constructed type names itself <c>Box&lt;&gt;&lt;int&gt;</c>. Neither is a string the
/// user can find in their own file, and a diagnostic naming a type they never wrote sends them
/// looking for it.
/// </para>
/// </summary>
internal static class TypeDisplay
{
    public static string Mist(IMistType type)
    {
        return type switch
        {
            // Before the general constructed case: an array is spelled with a rank suffix, not as
            // the Standard.Array<,> it is represented by, and its second argument is a length
            // rather than a type -- so the general case would render `int[]` as `Array<int, int>`.
            ConstructedType array when IsArray(array) => $"{Mist(array.TypeArguments[0])}{Rank(array.TypeArguments[1])}",
            ConstructedType { Enclosing: { } enclosing } nested => $"{Mist(enclosing)}.{Segment(nested)}",
            ConstructedType constructed => Segment(constructed),
            UnionType union => string.Join(" | ", union.Constituents.Select(Mist)),
            TupleType tuple => $"({string.Join(", ", tuple.ElementTypes.Select(Mist))})",
            NamedTupleType named => $"({string.Join(", ", named.ElementTypes.Select(Mist))})",
            FunctionType function => $"func<{string.Join(", ", function.TypeArgumentsInSourceOrder().Select(Mist))}>",

            // A dependent argument shows its domain, since its value is the thing not known.
            UnknownValueType unknownValue => Mist(unknownValue.Domain),
            RefinedType { IsAnonymous: true } refined => Mist(refined.BaseType),
            _ => MistType.StripArity(Keyword(type)),
        };
    }

    /// <summary>
    /// The reserved alias a Standard type is written with, so a diagnostic about <c>int</c> says
    /// <c>int</c> rather than <c>Int32</c>. Mirrors the alias table in
    /// <c>ScopeResolver.ResolveReservedAlias</c>, which is the mapping in the other direction.
    /// </summary>
    private static string Keyword(IMistType type)
    {
        if(type is not DeclaredType { NamespaceName: "Standard" })
        {
            return type.Name;
        }

        return type.Name switch
        {
            "Int32" => "int",
            "UInt32" => "uint",
            "Int64" => "long",
            "UInt64" => "ulong",
            "Int16" => "short",
            "UInt16" => "ushort",
            "Byte" => "byte",
            "SByte" => "sbyte",
            "Single" => "float",
            "Double" => "double",
            "Decimal" => "decimal",
            "Boolean" => "bool",
            "Char" => "char",
            "String" => "string",
            "Regex" => "regex",
            "Void" => "void",
            _ => type.Name,
        };
    }

    private static string Segment(ConstructedType constructed)
    {
        var name = MistType.StripArity(constructed.Definition.Name);

        return constructed.TypeArguments.Count == 0
            ? name
            : $"{name}<{string.Join(", ", constructed.TypeArguments.Select(Mist))}>"
            ;
    }

    private static bool IsArray(ConstructedType constructed)
    {
        return constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName
            && constructed.TypeArguments.Count == 2
            ;
    }

    /// <summary>
    /// The rank suffix for an array of the given length: <c>[5]</c> for a length the source stated,
    /// <c>[]</c> for one it did not.
    /// </summary>
    private static string Rank(IMistType length)
    {
        return length is LiftedType lifted ? $"[{lifted.Value}]" : "[]";
    }
}