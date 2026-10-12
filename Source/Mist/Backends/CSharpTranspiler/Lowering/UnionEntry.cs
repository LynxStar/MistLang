using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// How a value enters a union carrier holding constituents C# cannot tell apart. <c>int[3]</c> and
/// <c>int[5]</c> are distinct Mist types that both spell <c>int[]</c>, and C# selects a user-defined
/// conversion by the CLR type alone: two implicit operators from <c>int[]</c> are a duplicate, and one
/// would have to choose a tag the C# value cannot show. So such a constituent is entered through a named
/// factory, picked here from the value's Mist type -- the constituent identical to it, else the only one
/// it reaches for free. A value no single constituent claims is left to C#, which refuses it: choosing a
/// tag for it is a language decision, not a lowering.
/// </summary>
internal static class UnionEntry
{
    public static string? Factory(IMistType from, IMistType? slot)
    {
        if(slot is null
            || Carrier(slot) is not { } union
            || Carrier(from) is not null
            || Entered(TypeNameEmitter.Closed(from), union) is not { } constituent
            || !SharesSpelling(union, constituent))
        {
            return null;
        }

        return $"{UnionCarrierNames.CarrierName(union)}.{UnionCarrierNames.EntryFactory(constituent, union)}";
    }

    public static IReadOnlyList<IMistType>? Elements(IMistType? slot, TupleLiteralExpression literal)
    {
        return Slots(slot) is { } elements
            && elements.Count == literal.Elements.Count
            && literal.Elements.Where((element, i) => Enters(element, elements[i])).Any()
                ? elements
                : null
                ;
    }

    public static string? Decomposed(IMistType from, IMistType? slot, string bound)
    {
        if(Slots(slot) is not { Count: > 1 } slots
            || Unaliased(TypeNameEmitter.Closed(from)) is not ITupleType tuple
            || TypeNameEmitter.Expand(tuple.ElementTypes) is not { } elements
            || elements.Count != slots.Count)
        {
            return null;
        }

        var parts = new string[elements.Count];
        var entered = false;

        for(var i = 0; i < elements.Count; i++)
        {
            var read = $"{bound}.Item{i + 1}";

            if(Factory(elements[i], slots[i]) is { } factory)
            {
                parts[i] = $"{factory}({read})";
                entered = true;
            }
            else if(Decomposed(elements[i], slots[i], read) is { } nested)
            {
                parts[i] = nested;
                entered = true;
            }
            else
            {
                parts[i] = read;
            }
        }

        return entered ? $"({string.Join(", ", parts)})" : null;
    }

    private static IReadOnlyList<IMistType>? Slots(IMistType? slot)
    {
        return slot is not null && Unaliased(TypeNameEmitter.Closed(slot)) is ITupleType tuple
            ? TypeNameEmitter.Expand(tuple.ElementTypes)
            : null
            ;
    }

    private static bool Enters(Expression value, IMistType slot)
    {
        return Factory(value.Type, slot) is not null
            || (value is TupleLiteralExpression literal ? Elements(slot, literal) is not null : Decomposed(value.Type, slot, "") is not null);
    }

    public static bool SharesSpelling(UnionType union, IMistType constituent)
    {
        using var unannotated = TypeNameEmitter.Oblivious();
        var spelled = TypeNameEmitter.Emit(constituent);

        return union.Constituents.Count(other => TypeNameEmitter.Emit(other) == spelled) > 1;
    }

    public static IMistType? Entered(IMistType from, UnionType union)
    {
        var normalized = ConversionClassifier.Normalized(from, interner: null);

        if(union.Constituents.Where(constituent => ReferenceEquals(normalized, Unaliased(constituent)) || ConversionRelation.IsIdentity(normalized, Unaliased(constituent))).ToList() is [var identical])
        {
            return identical;
        }

        return union.Constituents.Where(constituent => ConversionClassifier.IsFree(from, constituent, interner: null)).ToList() is [var reached]
            ? reached
            : null
            ;
    }

    private static UnionType? Carrier(IMistType type)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is UnionType union && !OptionalUnion.IsOptional(union) ? union : null;
    }

    private static IMistType Unaliased(IMistType type)
    {
        return type is TypeAlias { AliasedType: { } aliased } ? Unaliased(aliased) : type;
    }
}