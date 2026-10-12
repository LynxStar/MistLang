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
internal static class MatchedSpaces
{
    private const int BoxLimit = 256;

    public static ValueAlgebra<object>? Of(IMistType input)
    {
        if(MatchedValues.Scalar(input) is { } scalar)
        {
            return scalar;
        }

        return Structure(Requirement.Unrefined(input)) switch
        {
            UnionType union when OptionalUnion.IsOptional(union) => Optional(union),
            UnionType union => Carrier(union),
            ITupleType tuple => Product(tuple),
            _ => null,
        };
    }

    private static ValueAlgebra<object> Element(IMistType type)
    {
        return Of(type) ?? Token(pattern => pattern is TypeTestPattern { Refinement: null } test && Same(test.NarrowedType, type));
    }

    private static ValueAlgebra<object> Optional(UnionType union)
    {
        var value = Element(union.Constituents.First(constituent => !OptionalUnion.IsVoid(constituent)));
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
        var boxes = new Boxes([.. tuple.ElementTypes.Select(Element)]);
        IReadOnlyList<object[]> everything = [boxes.Everything];

        return new ValueAlgebra<IReadOnlyList<object[]>>(
            everything, [],
            boxes.Meet,
            (left, right) => Limited([.. left, .. right]),
            values => boxes.Subtract(everything, values),
            (values, other) => boxes.Subtract(values, other) is { Count: 0 },
            (pattern, _) => pattern switch
            {
                TuplePattern positional => boxes.Read(positional),
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

    private static IMistType Structure(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Structure(aliased),
            INamedTupleType named => named.Underlying,
            _ => type,
        };
    }

    private static IReadOnlyList<object[]>? Limited(List<object[]> boxes)
    {
        return boxes.Count > BoxLimit ? null : boxes;
    }

    private sealed class Boxes(IReadOnlyList<ValueAlgebra<object>> elements)
    {
        public object[] Everything { get; } = [.. elements.Select(element => element.Everything)];

        public IReadOnlyList<object[]>? Read(TuplePattern positional)
        {
            if(positional.Elements.Count != elements.Count)
            {
                return null;
            }

            var box = new object[elements.Count];

            for(var i = 0; i < elements.Count; i++)
            {
                if(elements[i].Read(positional.Elements[i]) is not { } part)
                {
                    return null;
                }

                if(elements[i].IsEmpty(part))
                {
                    return [];
                }

                box[i] = part;
            }

            return [box];
        }

        public IReadOnlyList<object[]>? Meet(IReadOnlyList<object[]> left, IReadOnlyList<object[]> right)
        {
            var met = new List<object[]>();

            foreach(var mine in left)
            {
                foreach(var theirs in right)
                {
                    if(!Intersection(mine, theirs, out var box))
                    {
                        return null;
                    }

                    if(box is not null)
                    {
                        met.Add(box);
                    }
                }
            }

            return Limited(met);
        }

        public IReadOnlyList<object[]>? Subtract(IReadOnlyList<object[]> from, IReadOnlyList<object[]> removed)
        {
            var remaining = from;

            foreach(var cut in removed)
            {
                var next = new List<object[]>();

                foreach(var box in remaining)
                {
                    if(Difference(box, cut) is not { } pieces || next.Count + pieces.Count > BoxLimit)
                    {
                        return null;
                    }

                    next.AddRange(pieces);
                }

                remaining = next;
            }

            return remaining;
        }

        private bool Intersection(object[] mine, object[] theirs, out object[]? box)
        {
            box = null;
            var met = new object[elements.Count];

            for(var i = 0; i < elements.Count; i++)
            {
                if(elements[i].And(mine[i], theirs[i]) is not { } part)
                {
                    return false;
                }

                if(elements[i].IsEmpty(part))
                {
                    return true;
                }

                met[i] = part;
            }

            box = met;
            return true;
        }

        private List<object[]>? Difference(object[] box, object[] cut)
        {
            if(!Intersection(box, cut, out var met))
            {
                return null;
            }

            if(met is null)
            {
                return [box];
            }

            var pieces = new List<object[]>();

            for(var i = 0; i < elements.Count; i++)
            {
                if(elements[i].Not(cut[i]) is not { } outside || elements[i].And(box[i], outside) is not { } part)
                {
                    return null;
                }

                if(elements[i].IsEmpty(part))
                {
                    continue;
                }

                var piece = new object[elements.Count];

                for(var j = 0; j < elements.Count; j++)
                {
                    piece[j] = j < i ? met[j] : j == i ? part : box[j];
                }

                pieces.Add(piece);
            }

            return pieces;
        }
    }
}