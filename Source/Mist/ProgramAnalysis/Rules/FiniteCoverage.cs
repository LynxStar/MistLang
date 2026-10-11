using System.Globalization;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The values of a switch scrutinee that no arm given here admits, found by exact search rather than
/// guessed. Each arm reads as a box, one <see cref="Admitted"/> set per leaf of the scrutinee -- a
/// tuple's elements are its leaves, to any depth -- and a witness is a cell of the leaf domains that
/// lies inside no box. An arm this cannot read, a property pattern or a test of a type no leaf is, has
/// no box and ends the search with no verdict, since it may admit anything; so a value named here is
/// one no arm could match, never one the search failed to see covered. A leaf with no domain, a string
/// or a class, takes only a catch-all and is spelled `_` in a witness.
/// </summary>
internal static class FiniteCoverage
{
    private const int WitnessLimit = 8;
    private const int BoxLimit = 512;
    private const int CellBudget = 20_000;

    public static IReadOnlyList<string>? Missing(IMistType scrutinee, IEnumerable<Pattern> arms)
    {
        if(Shape.Of(scrutinee) is not { } shape)
        {
            return null;
        }

        var boxes = new List<Admitted[]>();

        foreach(var arm in arms)
        {
            if(Read(arm, shape) is not { } read || boxes.Count + read.Count > BoxLimit)
            {
                return null;
            }

            boxes.AddRange(read);
        }

        var leaves = shape.Leaves().ToList();
        var witnesses = new List<string>();
        var budget = CellBudget;

        return Search(shape, leaves, 0, boxes, new string[leaves.Count], witnesses, ref budget) ? witnesses : null;
    }

    private static IReadOnlyList<Admitted[]>? Read(Pattern pattern, Shape shape)
    {
        if(shape is Leaf leaf)
        {
            return leaf.Admits(pattern) is { } values ? [[values]] : null;
        }

        var product = (Product)shape;

        switch(pattern)
        {
            case CatchAllPattern:
                return [product.Everything()];

            case TypeTestPattern { Refinement: null } test when ConversionRelation.IsIdentity(test.NarrowedType, product.Type):
                return [product.Everything()];

            case TuplePattern tuple when tuple.Elements.Count == product.Elements.Count:
                {
                    IReadOnlyList<Admitted[]> boxes = [[]];

                    for(var i = 0; i < tuple.Elements.Count; i++)
                    {
                        if(Read(tuple.Elements[i], product.Elements[i]) is not { } element)
                        {
                            return null;
                        }

                        boxes = [.. boxes.SelectMany(prefix => element.Select(suffix => (Admitted[])[.. prefix, .. suffix]))];

                        if(boxes.Count > BoxLimit)
                        {
                            return null;
                        }
                    }

                    return boxes;
                }

            case DisjunctionPattern disjunction:
                {
                    var union = new List<Admitted[]>();

                    foreach(var operand in disjunction.Operands)
                    {
                        if(Read(operand, product) is not { } read)
                        {
                            return null;
                        }

                        union.AddRange(read);
                    }

                    return union;
                }

            case ConjunctionPattern conjunction:
                {
                    IReadOnlyList<Admitted[]> meet = [product.Everything()];

                    foreach(var operand in conjunction.Operands)
                    {
                        if(Read(operand, product) is not { } read)
                        {
                            return null;
                        }

                        meet = [.. meet.SelectMany(left => read.Select(right => left.Zip(right, (mine, theirs) => mine.And(theirs)).ToArray()))];
                    }

                    return meet;
                }

            default:
                return null;
        }
    }

    private static bool Search(
        Shape shape, IReadOnlyList<Leaf> leaves, int depth, IReadOnlyList<Admitted[]> boxes, string[] chosen, List<string> witnesses, ref int budget)
    {
        if(boxes.Count == 0)
        {
            Uncovered(shape, leaves, depth, chosen, witnesses);
            return true;
        }

        if(depth == leaves.Count)
        {
            return true;
        }

        foreach(var cell in leaves[depth].Cells(boxes.Select(box => box[depth])))
        {
            if(--budget < 0)
            {
                return false;
            }

            chosen[depth] = leaves[depth].Describe(cell);

            if(!Search(shape, leaves, depth + 1, [.. boxes.Where(box => box[depth].Contains(cell.Low))], chosen, witnesses, ref budget))
            {
                return false;
            }

            if(witnesses.Count >= WitnessLimit)
            {
                return true;
            }
        }

        return true;
    }

    private static void Uncovered(Shape shape, IReadOnlyList<Leaf> leaves, int depth, string[] chosen, List<string> witnesses)
    {
        for(var i = depth; i < leaves.Count; i++)
        {
            chosen[i] = "_";
        }

        if(depth == 0 && leaves.Count > 0 && leaves[0].Points() is { } points)
        {
            foreach(var point in points)
            {
                chosen[0] = leaves[0].Describe(point);
                witnesses.Add(shape.Render(chosen));
            }

            return;
        }

        witnesses.Add(shape.Render(chosen));
    }

    private abstract class Shape
    {
        public static Shape? Of(IMistType type)
        {
            return type switch
            {
                ErrorType => null,
                TypeAlias { AliasedType: { } aliased } => Of(aliased),
                INamedTupleType named => Of(named.Underlying),
                ITupleType tuple => new Product(tuple, [.. tuple.ElementTypes.Select(element => Of(element) ?? new Leaf(element))]),
                _ => new Leaf(type),
            };
        }

        public abstract IEnumerable<Leaf> Leaves();

        public abstract string Render(string[] chosen, ref int next);

        public string Render(string[] chosen)
        {
            var next = 0;
            return Render(chosen, ref next);
        }
    }

    private sealed class Leaf(IMistType type) : Shape
    {
        public IMistType Type { get; } = type;

        public Admitted? Domain { get; } = MatchedValues.Domain(type);

        public Admitted Everything => Domain ?? Admitted.Everything;

        public Admitted? Admits(Pattern pattern)
        {
            if(Domain is not null)
            {
                return MatchedValues.Admits(pattern, Type);
            }

            return pattern is CatchAllPattern || (pattern is TypeTestPattern { Refinement: null } test && ConversionRelation.IsIdentity(test.NarrowedType, Type))
                ? Admitted.Everything
                : null;
        }

        public IEnumerable<long>? Points()
        {
            var unrefined = Requirement.Unrefined(Type);

            if(Domain is null || (unrefined is not EnumType && unrefined.FullyQualifiedName != WellKnownTypes.Boolean.FullyQualifiedName))
            {
                return null;
            }

            return Domain.Intervals.SelectMany(interval => Enumerable.Range(0, (int)(interval.High - interval.Low + 1)).Select(offset => interval.Low + offset));
        }

        public IEnumerable<Interval> Cells(IEnumerable<Admitted> sets)
        {
            if(Points() is { } points)
            {
                return points.Select(point => new Interval(point, point));
            }

            var cuts = new SortedSet<long>();

            foreach(var interval in Everything.Intervals.Concat(sets.SelectMany(set => set.Intervals)))
            {
                _ = cuts.Add(interval.Low);

                if(interval.High < long.MaxValue)
                {
                    _ = cuts.Add(interval.High + 1);
                }
            }

            var edges = cuts.ToList();
            var cells = new List<Interval>();

            for(var i = 0; i < edges.Count; i++)
            {
                var high = i + 1 < edges.Count ? edges[i + 1] - 1 : long.MaxValue;
                cells.AddRange(Everything.And(Admitted.Between(edges[i], high)).Intervals);
            }

            return cells;
        }

        public string Describe(Interval cell)
        {
            return Domain is null ? "_" : Describe(cell.Low > 0 ? cell.Low : cell.High < 0 ? cell.High : 0);
        }

        public string Describe(long value)
        {
            var unrefined = Requirement.Unrefined(Type);

            if(unrefined is EnumType enumType)
            {
                return enumType.MembersList[(int)value].Name;
            }

            if(unrefined.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName)
            {
                return value == 1 ? "true" : "false";
            }

            if(unrefined.FullyQualifiedName == WellKnownTypes.Char.FullyQualifiedName)
            {
                var character = (char)value;

                return character switch
                {
                    '\'' => "'\\''",
                    '\\' => "'\\\\'",
                    >= ' ' and <= '~' => $"'{character}'",
                    _ => $"'\\u{value:X4}'",
                };
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        public override IEnumerable<Leaf> Leaves()
        {
            yield return this;
        }

        public override string Render(string[] chosen, ref int next)
        {
            return chosen[next++];
        }
    }

    private sealed class Product(ITupleType type, IReadOnlyList<Shape> elements) : Shape
    {
        public ITupleType Type { get; } = type;

        public IReadOnlyList<Shape> Elements { get; } = elements;

        public Admitted[] Everything()
        {
            return [.. Leaves().Select(leaf => leaf.Everything)];
        }

        public override IEnumerable<Leaf> Leaves()
        {
            return Elements.SelectMany(element => element.Leaves());
        }

        public override string Render(string[] chosen, ref int next)
        {
            var parts = new List<string>();

            foreach(var element in Elements)
            {
                parts.Add(element.Render(chosen, ref next));
            }

            return $"({string.Join(", ", parts)})";
        }
    }
}