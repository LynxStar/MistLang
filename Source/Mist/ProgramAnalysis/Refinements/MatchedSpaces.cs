using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// A union carrier has one slot no constituent arm fills: C# sees those arms as property patterns on the
/// carrier and runs a catch-all after all of them, and refusing that catch-all is an open policy, not a
/// containment fact.
/// </summary>
internal static partial class MatchedSpaces
{
    private const int BoxLimit = 256;

    public static ValueAlgebra<object>? Of(IMistType input)
    {
        return Of(input, nullable: true);
    }

    // A reference C# judges may be null, which no structural arm reaches, so a class or sequence read
    // directly -- the scrutinee, a tuple element, a list element, a member -- keeps a slot only a catch-all
    // or a negation fills: Covered refuses nothing C# would run. An optional's held value has no such
    // slot, its absence being that null.
    private static ValueAlgebra<object>? Of(IMistType input, bool nullable)
    {
        if(MatchedValues.Scalar(input) is { } scalar)
        {
            return scalar;
        }

        var structure = Structure(Requirement.Unrefined(input));

        return structure switch
        {
            UnionType union when OptionalUnion.IsOptional(union) => Optional(union),
            UnionType union => Carrier(union),
            ITupleType tuple => Product(tuple),
            _ when Lengths.IsSized(structure) => Sequence(structure, nullable),
            _ => Members(structure, nullable),
        };
    }

    private static ValueAlgebra<object> Element(IMistType type, bool nullable = true)
    {
        return Of(type, nullable) ?? Token(pattern => pattern is TypeTestPattern { Refinement: null } test && Same(test.NarrowedType, type));
    }

    private static ValueAlgebra<object> WithNull(ValueAlgebra<object> value)
    {
        var absent = Token(_ => false);

        return Sum([value, absent], pattern => value.Primary(pattern, value) is { } held ? [held, absent.Nothing] : null);
    }

    private static bool IsReference(IMistType structure)
    {
        return ((structure as ConstructedType)?.Definition ?? structure) is ClassType or AbstractType;
    }

    private static ValueAlgebra<object> Optional(UnionType union)
    {
        var value = Element(union.Constituents.First(constituent => !OptionalUnion.IsVoid(constituent)), nullable: false);
        var absent = Token(_ => false);

        return Sum([value, absent], pattern => pattern is TypeTestPattern { TestsAbsence: true }
            ? [value.Nothing, absent.Everything]
            : value.Primary(pattern, value) is { } held ? [held, absent.Nothing] : null);
    }

    private static ValueAlgebra<object> Carrier(UnionType union)
    {
        var slots = Enumerable.Range(0, union.Constituents.Count + 1).Select(_ => Token(_ => false)).ToList();

        return Sum(slots, pattern =>
        {
            var test = pattern switch
            {
                TypeTestPattern typeTest => typeTest,
                PropertyPattern { TypeTest: { } typeTest, Subpatterns.Count: 0 } => typeTest,
                _ => null,
            };

            for(var i = 0; i < union.Constituents.Count; i++)
            {
                if(test?.Constituent is { } constituent && ReferenceEquals(constituent, union.Constituents[i]) && Same(test.NarrowedType, constituent))
                {
                    return [.. slots.Select((slot, j) => j == i ? slot.Everything : slot.Nothing)];
                }
            }

            return null;
        });
    }

    private static ValueAlgebra<object> Sum(IReadOnlyList<ValueAlgebra<object>> slots, Func<Pattern, object[]?> primary)
    {
        return new ValueAlgebra<object[]>(
            [.. slots.Select(slot => slot.Everything)],
            [.. slots.Select(slot => slot.Nothing)],
            (left, right) => Zip(slots, left, right, (slot, mine, theirs) => slot.And(mine, theirs)),
            (left, right) => Zip(slots, left, right, (slot, mine, theirs) => slot.Or(mine, theirs)),
            values => Zip(slots, values, values, (slot, mine, _) => slot.Not(mine)),
            (values, other) => slots.Select((slot, i) => slot.IsSubsetOf(values[i], other[i])).All(subset => subset),
            (pattern, _) => primary(pattern)).Erased();
    }

    private static object[]? Zip(
        IReadOnlyList<ValueAlgebra<object>> slots, object[] left, object[] right, Func<ValueAlgebra<object>, object, object, object?> combine)
    {
        var result = new object[slots.Count];

        for(var i = 0; i < slots.Count; i++)
        {
            if(combine(slots[i], left[i], right[i]) is not { } part)
            {
                return null;
            }

            result[i] = part;
        }

        return result;
    }

    private static ValueAlgebra<object> Product(ITupleType tuple)
    {
        var boxes = new Boxes(tuple.ElementTypes.Select(type => Element(type)));
        IReadOnlyList<object?[]> everything = [Boxes.Everything];

        return new ValueAlgebra<IReadOnlyList<object?[]>>(
            everything, [],
            boxes.Meet,
            (left, right) => Limited([.. left, .. right]),
            values => boxes.Subtract(everything, values),
            (values, other) => boxes.Subtract(values, other) is { Count: 0 },
            (pattern, _) => pattern switch
            {
                TuplePattern positional when positional.Elements.Count == boxes.Count => boxes.Read(positional.Elements),
                TypeTestPattern { Refinement: null } test when Same(test.NarrowedType, tuple) => everything,
                _ => null,
            }).Erased();
    }

    private static ValueAlgebra<object> Token(Func<Pattern, bool> whole)
    {
        var one = Admitted.Exactly(0);

        return new ValueAlgebra<Admitted>(
            one, Admitted.Nothing,
            (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not().And(one),
            (values, other) => values.IsSubsetOf(other),
            (pattern, self) => whole(pattern) ? self.Everything : null).Erased();
    }

    private static bool Same(IMistType tested, IMistType type)
    {
        return ReferenceEquals(Structure(tested), Structure(type)) || ConversionRelation.IsIdentity(Structure(tested), Structure(type));
    }

    private static bool Tests(TypeTestPattern test, IMistType type)
    {
        return !test.TestsAbsence && Same(Requirement.Unrefined(test.NarrowedType), type);
    }

    private static IMistType Structure(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Structure(aliased),
            INamedTupleType named => named.Underlying,
            _ => type,
        };
    }

    private static IReadOnlyList<object?[]>? Limited(List<object?[]> boxes)
    {
        return boxes.Count > BoxLimit ? null : boxes;
    }
}