using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Refinements;

internal static partial class MatchedSpaces
{
    /// <summary>
    /// A dimension a box does not reach -- past its end, or null -- is everything there, so a box built
    /// before a later pattern named a further member stays exact once the dimension exists.
    /// </summary>
    private sealed class Boxes(IEnumerable<ValueAlgebra<object>> elements)
    {
        private readonly List<ValueAlgebra<object>> dimensions = [.. elements];

        public int Count => dimensions.Count;

        public static object?[] Everything => [];

        public ValueAlgebra<object> this[int dimension] => dimensions[dimension];

        public int Add(ValueAlgebra<object> element)
        {
            dimensions.Add(element);
            return dimensions.Count - 1;
        }

        public object At(object?[] box, int dimension)
        {
            return dimension < box.Length && box[dimension] is { } part ? part : dimensions[dimension].Everything;
        }

        public IReadOnlyList<object?[]>? Read(IReadOnlyList<Pattern> parts)
        {
            var box = new object?[dimensions.Count];

            for(var i = 0; i < dimensions.Count; i++)
            {
                if(dimensions[i].Read(parts[i]) is not { } part)
                {
                    return null;
                }

                if(dimensions[i].IsEmpty(part))
                {
                    return [];
                }

                box[i] = part;
            }

            return [box];
        }

        public IReadOnlyList<object?[]>? Meet(IReadOnlyList<object?[]> left, IReadOnlyList<object?[]> right)
        {
            var met = new List<object?[]>();

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

        public IReadOnlyList<object?[]>? Subtract(IReadOnlyList<object?[]> from, IReadOnlyList<object?[]> removed)
        {
            var remaining = from;

            foreach(var cut in removed)
            {
                var next = new List<object?[]>();

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

        private bool Intersection(object?[] mine, object?[] theirs, out object?[]? box)
        {
            box = null;
            var met = new object?[dimensions.Count];

            for(var i = 0; i < dimensions.Count; i++)
            {
                if(dimensions[i].And(At(mine, i), At(theirs, i)) is not { } part)
                {
                    return false;
                }

                if(dimensions[i].IsEmpty(part))
                {
                    return true;
                }

                met[i] = part;
            }

            box = met;
            return true;
        }

        private List<object?[]>? Difference(object?[] box, object?[] cut)
        {
            if(!Intersection(box, cut, out var met))
            {
                return null;
            }

            if(met is null)
            {
                return [box];
            }

            var pieces = new List<object?[]>();

            for(var i = 0; i < dimensions.Count; i++)
            {
                if(dimensions[i].Not(At(cut, i)) is not { } outside || dimensions[i].And(At(box, i), outside) is not { } part)
                {
                    return null;
                }

                if(dimensions[i].IsEmpty(part))
                {
                    continue;
                }

                var piece = new object?[dimensions.Count];

                for(var j = 0; j < dimensions.Count; j++)
                {
                    piece[j] = j < i ? met[j] : j == i ? part : At(box, j);
                }

                pieces.Add(piece);
            }

            return pieces;
        }
    }
}