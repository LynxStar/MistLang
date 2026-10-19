using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Refinements;

internal static partial class MatchedSpaces
{
    private static ValueAlgebra<object> Sequence(IMistType structure, bool nullable)
    {
        var reader = new SequenceReader();
        var everything = new Sequences(0, 0, [], [[]]);
        var nothing = new Sequences(0, 0, [], []);

        var space = new ValueAlgebra<Sequences>(
            everything, nothing,
            reader.And,
            reader.Or,
            values => reader.Subtract(everything, values),
            (values, other) => reader.Subtract(values, other) is { Count: 0 },
            (pattern, self) => pattern switch
            {
                ListPattern { IsFailed: false, Reads: null, Slice: null } list => reader.Read(list),
                PropertyPattern { IsFailed: false, Subpatterns.Count: 0, TypeTest: null } => self.Everything,
                PropertyPattern { IsFailed: false, Subpatterns.Count: 0, TypeTest: { } test } when Tests(test, structure) => MatchedValues.Tested(test, self),
                TypeTestPattern test when Tests(test, structure) => MatchedValues.Tested(test, self),
                _ => null,
            }).Erased();

        return nullable && IsReference(structure) ? WithNull(space) : space;
    }

    /// <summary>
    /// Sequences a list pattern admits: for each length below the threshold a product over that many
    /// positions, and for every length at or above it one product over the length, <see cref="Prefix"/>
    /// positions counted from the start and <see cref="Suffix"/> counted from the end -- which are
    /// distinct elements exactly when the length is at least their sum, so the threshold is where that
    /// shape becomes exact for every box in the set.
    /// </summary>
    private sealed record Sequences(int Prefix, int Suffix, IReadOnlyList<IReadOnlyList<object?[]>> Finite, IReadOnlyList<object?[]> Tail)
    {
        public int Threshold => Prefix + Suffix;

        public int Count => Finite.Sum(boxes => boxes.Count) + Tail.Count;
    }

    private sealed class SequenceReader
    {
        private IMistType? elementType;
        private ValueAlgebra<object>? element;

        public Sequences? Read(ListPattern list)
        {
            var slice = -1;

            for(var i = 0; i < list.Elements.Count; i++)
            {
                if(!list.Elements[i].IsSlice)
                {
                    continue;
                }

                if(slice >= 0 || !Open(list.Elements[i]))
                {
                    return null;
                }

                slice = i;
            }

            var prefix = slice < 0 ? list.Elements.Count : slice;
            var suffix = slice < 0 ? 0 : list.Elements.Count - slice - 1;
            var box = new object?[1 + prefix + suffix];
            box[0] = slice < 0 ? Admitted.Exactly(prefix) : Admitted.AtLeast(prefix + suffix);
            var position = 1;

            foreach(var positional in list.Elements.Where(candidate => !candidate.IsSlice))
            {
                if(positional is not { Subpattern: { } subpattern, ScrutineeType: { } scrutinee } || !Establish(scrutinee)
                    || element!.Read(subpattern) is not { } part)
                {
                    return null;
                }

                if(element.IsEmpty(part))
                {
                    return new Sequences(0, 0, [], []);
                }

                box[position++] = part;
            }

            return new Sequences(prefix, suffix, [.. Enumerable.Repeat<IReadOnlyList<object?[]>>([], prefix + suffix)], [box]);
        }

        public Sequences? And(Sequences left, Sequences right)
        {
            return Combine(left, right, (boxes, mine, theirs) => boxes.Meet(mine, theirs));
        }

        public Sequences? Or(Sequences left, Sequences right)
        {
            return Combine(left, right, (_, mine, theirs) => [.. mine, .. theirs]);
        }

        public Sequences? Subtract(Sequences from, Sequences removed)
        {
            return Combine(from, removed, (boxes, mine, theirs) => boxes.Subtract(mine, theirs));
        }

        private Sequences? Combine(
            Sequences left, Sequences right, Func<Boxes, IReadOnlyList<object?[]>, IReadOnlyList<object?[]>, IReadOnlyList<object?[]>?> combine)
        {
            var prefix = Math.Max(left.Prefix, right.Prefix);
            var suffix = Math.Max(left.Suffix, right.Suffix);

            if(Normalised(left, prefix, suffix) is not { } mine || Normalised(right, prefix, suffix) is not { } theirs)
            {
                return null;
            }

            var finite = new List<IReadOnlyList<object?[]>>();

            for(var length = 0; length < prefix + suffix; length++)
            {
                if(combine(Positions(length), mine.Finite[length], theirs.Finite[length]) is not { } boxes)
                {
                    return null;
                }

                finite.Add(boxes);
            }

            return combine(Tail(prefix, suffix), mine.Tail, theirs.Tail) is { } tail
                ? Limited(new Sequences(prefix, suffix, finite, tail))
                : null;
        }

        private Sequences? Normalised(Sequences set, int prefix, int suffix)
        {
            if(set.Prefix == prefix && set.Suffix == suffix)
            {
                return set;
            }

            var from = Tail(set.Prefix, set.Suffix);
            var to = Tail(prefix, suffix);
            var finite = new List<IReadOnlyList<object?[]>>();

            for(var length = 0; length < to.Count - 1; length++)
            {
                finite.Add(length < set.Threshold ? set.Finite[length] : Expanded(set, from, length));
            }

            var tail = new List<object?[]>();

            foreach(var box in set.Tail)
            {
                var lengths = ((Admitted)from.At(box, 0)).And(Admitted.AtLeast(prefix + suffix));

                if(lengths.Intervals.Length == 0)
                {
                    continue;
                }

                var widened = new object?[to.Count];
                widened[0] = lengths;

                for(var i = 0; i < set.Prefix; i++)
                {
                    widened[1 + i] = from.At(box, 1 + i);
                }

                for(var j = 0; j < set.Suffix; j++)
                {
                    widened[1 + prefix + j] = from.At(box, 1 + set.Prefix + j);
                }

                tail.Add(widened);
            }

            return Limited(new Sequences(prefix, suffix, finite, tail));
        }

        private static List<object?[]> Expanded(Sequences set, Boxes from, int length)
        {
            var boxes = new List<object?[]>();

            foreach(var box in set.Tail)
            {
                if(!((Admitted)from.At(box, 0)).Contains(length))
                {
                    continue;
                }

                var positions = new object?[length];

                for(var i = 0; i < set.Prefix; i++)
                {
                    positions[i] = from.At(box, 1 + i);
                }

                for(var j = 0; j < set.Suffix; j++)
                {
                    positions[length - set.Suffix + j] = from.At(box, 1 + set.Prefix + j);
                }

                boxes.Add(positions);
            }

            return boxes;
        }

        private static Sequences? Limited(Sequences set)
        {
            return set.Count > BoxLimit ? null : set;
        }

        private static bool Open(ListPatternElement slice)
        {
            return slice.Subpattern switch
            {
                null or CatchAllPattern => true,
                TypeTestPattern { Refinement: null } test when slice.ScrutineeType is { } sliced => Tests(test, Structure(Requirement.Unrefined(sliced))),
                _ => false,
            };
        }

        private bool Establish(IMistType scrutinee)
        {
            if(element is null)
            {
                elementType = scrutinee;
                element = Element(scrutinee);
                return true;
            }

            return ReferenceEquals(scrutinee, elementType) || Same(Requirement.Unrefined(scrutinee), Requirement.Unrefined(elementType!));
        }

        private Boxes Positions(int length)
        {
            return new Boxes(Enumerable.Repeat(element ?? Token(_ => false), length));
        }

        private Boxes Tail(int prefix, int suffix)
        {
            return new Boxes([LengthDomain(prefix + suffix), .. Enumerable.Repeat(element ?? Token(_ => false), prefix + suffix)]);
        }

        private static ValueAlgebra<object> LengthDomain(int threshold)
        {
            var domain = Admitted.AtLeast(threshold);

            return new ValueAlgebra<Admitted>(
                domain, Admitted.Nothing,
                (left, right) => left.And(right), (left, right) => left.Or(right), values => values.Not().And(domain),
                (values, other) => values.IsSubsetOf(other),
                (_, _) => null).Erased();
        }
    }
}