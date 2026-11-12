using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

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
        return Render(Read(type), spell: null);
    }

    /// <summary>
    /// The types of one message, spelled as <see cref="Mist(IMistType)"/> spells them except where two
    /// distinct type parameters it shows share a name -- an enclosing method's <c>T</c> beside the callee's --
    /// which are then named with their declaration, <c>T of method Api.Pick</c>, so the reader can tell them
    /// apart. Where that still reads alike, as for two overloads' <c>T</c>, the declaration is spelled in full:
    /// the method's parameter types, the type's namespace. Only the parameters actually shown count, so a
    /// message with no such pair is unchanged.
    /// </summary>
    public static IReadOnlyList<string> Distinguished(IReadOnlyList<IMistType> types)
    {
        var shown = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);
        var plain = types.Select(type => Render(Read(type), parameter =>
        {
            _ = shown.Add(parameter);
            return null;
        })).ToList();
        var labels = shown.GroupBy(parameter => parameter.Name).Where(named => named.Count() > 1).SelectMany(named => named)
            .ToDictionary<TypeParameter, TypeParameter, string>(parameter => parameter, parameter => Declared(parameter, detailed: false),
                ReferenceEqualityComparer.Instance);

        foreach(var tied in labels.GroupBy(label => label.Value).Where(alike => alike.Count() > 1).SelectMany(alike => alike).ToList())
        {
            labels[tied.Key] = Declared(tied.Key, detailed: true);
        }

        return labels.Count == 0
            ? plain
            : [.. types.Select(type => Render(Read(type), parameter => labels.GetValueOrDefault(parameter)))];
    }

    /// <summary>
    /// True when the type is a slot's argument standing alone, or holds a capture or <c>unknown</c>
    /// outside a slot: the type of a member declared by the dependent parameter, <c>meter.Value</c> on
    /// <c>public N Value</c>, which a program sees as the slot's domain. <c>string s = meter.Value;</c>
    /// is refused for an <c>int</c>, not for the <c>4</c> or the <c>n</c> the meter was closed with, and
    /// so is <c>(meter.Value, 1)</c> for an <c>(int, int)</c>. A read of the slot itself,
    /// <c>ring.Capacity</c>, is typed by the slot's contract and never reaches here. Inside a slot the
    /// argument is the value itself, so <c>int[n]</c> and <c>Ring&lt;4&gt;</c> are spelled as written;
    /// so is a literal inside any other type, since <c>Box&lt;4&gt;</c> is a type the source writes
    /// itself and the same model type as a member's value inferred into that position.
    /// </summary>
    public static bool HoldsSlotRead(IMistType type)
    {
        return type is LiftedType { Value: not TypePackValue } || HoldsValue(type);
    }

    private static bool HoldsValue(IMistType type)
    {
        return type switch
        {
            CapturedValue or UnknownValueType => true,
            ConstructedType constructed => constructed.TypeArguments.Where((argument, position) => !IsSlot(constructed, position) && HoldsValue(argument)).Any()
                || (constructed.Enclosing is { } enclosing && HoldsValue(enclosing)),
            UnionType union => union.Constituents.Any(HoldsValue),
            ITupleType tuple => tuple.ElementTypes.Any(HoldsValue),
            FunctionType function => function.ParameterTypes.Any(HoldsValue) || HoldsValue(function.ReturnType),
            RefinedType refined when refined.IsAnonymous || refined.IsSubstituted => HoldsValue(refined.BaseType),
            _ => false,
        };
    }

    private static IMistType Read(IMistType type)
    {
        return type is LiftedType { Value: not TypePackValue } lifted ? lifted.Type : type;
    }

    private static string Render(IMistType type, Func<TypeParameter, string?>? spell)
    {
        string mist(IMistType inner)
        {
            return Render(inner, spell);
        }

        return type switch
        {
            LiftedType { Value: not TypePackValue } lifted => lifted.Name,
            CapturedValue captured => mist(captured.Domain),
            UnknownValueType unknownValue => mist(unknownValue.Domain),

            // Before the general constructed case: an array is spelled with a rank suffix, not as
            // the Standard.Array<,> it is represented by, and its second argument is a length
            // rather than a type -- so the general case would render `int[]` as `Array<int, int>`.
            ConstructedType array when IsArray(array) => $"{mist(array.TypeArguments[0])}{Rank(array.TypeArguments[1])}",
            ConstructedType { Enclosing: { } enclosing } nested => $"{mist(enclosing)}.{Segment(nested, spell)}",
            ConstructedType constructed => Segment(constructed, spell),
            UnionType union => string.Join(" | ", union.Constituents.Select(mist)),
            TupleType tuple => $"({string.Join(", ", tuple.ElementTypes.Select(mist))})",
            NamedTupleType named => $"({string.Join(", ", named.ElementTypes.Select(mist))})",
            FunctionType function => $"{(function.IsPure ? "pure " : string.Empty)}func<{string.Join(", ", function.TypeArgumentsInSourceOrder().Select(mist))}>",
            RefinedType refined when refined.IsAnonymous || refined.IsSubstituted => mist(refined.BaseType),
            TypeParameter parameter when spell?.Invoke(parameter) is { } spelled => spelled,
            _ => MistType.StripArity(Keyword(type)),
        };
    }

    private static string Written(IMistType argument, Func<TypeParameter, string?>? spell)
    {
        return argument switch
        {
            LiftedType lifted => lifted.Name,
            CapturedValue captured => captured.WrittenName,
            _ => Render(argument, spell),
        };
    }

    private static bool IsSlot(ConstructedType constructed, int position)
    {
        return constructed.Definition is IHasTypeParameters { TypeParametersList: var parameters }
            && parameters.Find(parameter => parameter.Ordinal == position) is { IsDependent: true, IsPack: false };
    }

    private static string Declared(TypeParameter parameter, bool detailed)
    {
        var declaration = parameter switch
        {
            { DeclaringMethod: { } method } => DeclaredBy(method, detailed),
            { DeclaringType: { } type } => $"type {MistType.StripArity(detailed ? type.FullyQualifiedName : type.Name)}",
            { Scope: ExtensionScope { Block: { } block } } =>
                $"extension({(block.ResolvedReceiverType is { } receiver ? Mist(receiver) : "")} {block.ReceiverParameterName})",
            _ => null,
        };
        var name = MistType.StripArity(parameter.Name);

        return declaration is null ? name : $"{name} of {declaration}";
    }

    private static string DeclaredBy(Method method, bool detailed)
    {
        var kind = (method.DeclaringBlock ?? (method.Scope?.Parent as ExtensionScope)?.Block) is null ? "method" : "extension method";
        var owner = method.DeclaringType is { } type ? $"{MistType.StripArity(detailed ? type.FullyQualifiedName : type.Name)}." : "";
        var signature = detailed ? $"({string.Join(", ", method.ParametersList.Select(parameter => Mist(parameter.Type)))})" : "";

        return $"{kind} {owner}{method.Name}{signature}";
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

    private static string Segment(ConstructedType constructed, Func<TypeParameter, string?>? spell)
    {
        var name = MistType.StripArity(constructed.Definition.Name);

        return constructed.TypeArguments.Count == 0
            ? name
            : $"{name}<{string.Join(", ", constructed.TypeArguments.Select((argument, position) => IsSlot(constructed, position) ? Written(argument, spell) : Render(argument, spell)))}>"
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
        return length switch
        {
            LiftedType lifted => $"[{lifted.Name}]",
            CapturedValue captured => $"[{captured.WrittenName}]",
            _ => "[]",
        };
    }
}