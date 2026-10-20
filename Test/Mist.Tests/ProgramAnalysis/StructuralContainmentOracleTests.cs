using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// The universe holds a value in every region the catalogue's constants cut, every length up to one past
// the widest prefix-plus-suffix, and C#'s null for a reference, so a finite enumeration decides containment.
[Trait("Tier", "1")]
public sealed class StructuralContainmentOracleTests
{
    private static readonly long[] Values = [-1, 0, 1, 2];

    private readonly HandBuiltRefinements h = new();

    [Fact]
    public void ListContainment_AgreesWithEnumerationOverEveryCataloguePair()
    {
        var numbers = h.Sequence();
        var catalogue = ListCatalogue(numbers);
        var universe = new List<object?> { null };

        for(var length = 0; length <= 4; length++)
        {
            universe.AddRange(Sequences(length).Select(sequence => (object?)sequence));
        }

        AssertAgreement(numbers, catalogue, universe);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyContainment_AgreesWithEnumerationOverEveryCataloguePair(bool valueType)
    {
        MemberType point = valueType ? HandBuiltModel.Struct("Point") : HandBuiltModel.Class("Point");
        var x = Member(point, "X");
        var y = Member(point, "Y");
        var catalogue = PropertyCatalogue(point, x, y);
        var universe = new List<object?>();

        if(!valueType)
        {
            universe.Add(null);
        }

        foreach(var first in Values)
        {
            foreach(var second in Values)
            {
                universe.Add(new Dictionary<object, long>(ReferenceEqualityComparer.Instance) { [x] = first, [y] = second });
            }
        }

        AssertAgreement(point, catalogue, universe);
    }

    private void AssertAgreement(IMistType input, IReadOnlyList<(string Text, Pattern Pattern)> catalogue, IReadOnlyList<object?> universe)
    {
        var disagreements = new List<string>();
        var earlierSets = catalogue.Select(entry => new[] { entry })
            .Concat(catalogue.SelectMany((first, i) => catalogue.Skip(i + 1).Select(second => new[] { first, second })))
            .ToList();

        foreach(var earlier in earlierSets)
        {
            foreach(var later in catalogue)
            {
                var reached = universe.Where(value => Matches(later.Pattern, value)).ToList();
                var expected = earlier.Any(entry => entry.Pattern is CatchAllPattern)
                    || (reached.Count > 0 && reached.All(value => earlier.Any(entry => Matches(entry.Pattern, value))));
                var actual = PatternAnalyzer.ArmFinding([.. earlier.Select(entry => entry.Pattern)], later.Pattern, input, h.Built.Interner) is not null;

                if(expected != actual)
                {
                    disagreements.Add($"{string.Join(", ", earlier.Select(entry => entry.Text))} then {later.Text}: oracle {expected}, algebra {actual}");
                }
            }
        }

        Assert.True(disagreements.Count == 0, string.Join(Environment.NewLine, disagreements));
    }

    private static bool Matches(Pattern pattern, object? value)
    {
        return pattern switch
        {
            CatchAllPattern => true,
            NegatedPattern negated => !Matches(negated.Operand, value),
            ConjunctionPattern conjunction => conjunction.Operands.All(operand => Matches(operand, value)),
            DisjunctionPattern disjunction => disjunction.Operands.Any(operand => Matches(operand, value)),
            ComparisonPattern { Bound: LiteralExpression { Value: int bound } } comparison => value is long number && Holds(comparison.Kind, number, bound),
            TypeTestPattern => value is not null,
            PropertyPattern property => value is Dictionary<object, long> members
                && property.Subpatterns.All(subpattern => Matches(subpattern.Pattern, members[((PropertyResult)subpattern.Member).Property])),
            ListPattern list => value is long[] sequence && MatchesList(list, sequence),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    private static bool MatchesList(ListPattern list, long[] sequence)
    {
        var slice = list.Elements.ToList().FindIndex(element => element.IsSlice);

        if(slice < 0)
        {
            return sequence.Length == list.Elements.Count
                && list.Elements.Select((element, i) => Matches(element.Subpattern!, sequence[i])).All(matched => matched);
        }

        var suffix = list.Elements.Count - slice - 1;

        if(sequence.Length < slice + suffix)
        {
            return false;
        }

        for(var i = 0; i < slice; i++)
        {
            if(!Matches(list.Elements[i].Subpattern!, sequence[i]))
            {
                return false;
            }
        }

        for(var j = 0; j < suffix; j++)
        {
            if(!Matches(list.Elements[slice + 1 + j].Subpattern!, sequence[sequence.Length - suffix + j]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Holds(ComparisonKind kind, long value, int bound)
    {
        return kind switch
        {
            ComparisonKind.Equal => value == bound,
            ComparisonKind.LessThan => value < bound,
            ComparisonKind.LessThanOrEqual => value <= bound,
            ComparisonKind.GreaterThan => value > bound,
            _ => value >= bound,
        };
    }

    private static IEnumerable<long[]> Sequences(int length)
    {
        if(length == 0)
        {
            yield return [];
            yield break;
        }

        foreach(var shorter in Sequences(length - 1))
        {
            foreach(var value in Values)
            {
                yield return [.. shorter, value];
            }
        }
    }

    private List<(string Text, Pattern Pattern)> ListCatalogue(IMistType numbers)
    {
        var integer = h.Int;
        Pattern slice = new CatchAllPattern(HandBuiltModel.Node(), numbers, null);

        ListPattern list(params Pattern?[] elements)
        {
            return new ListPattern(HandBuiltModel.Node(), [.. elements.Select(element => element is null
                ? new ListPatternElement(null, true, null)
                : ReferenceEquals(element, slice)
                    ? new ListPatternElement(element, true, numbers)
                    : new ListPatternElement(element, false, integer))], numbers);
        }

        return
        [
            ("[]", list()),
            ("[_]", list(Any(integer))),
            ("[1]", list(Equal(1))),
            ("[0, 1]", list(Equal(0), Equal(1))),
            ("[1, 1]", list(Equal(1), Equal(1))),
            ("[_, _]", list(Any(integer), Any(integer))),
            ("[_, ..]", list(Any(integer), null)),
            ("[.., _]", list(null, Any(integer))),
            ("[1, ..]", list(Equal(1), null)),
            ("[.., 1]", list(null, Equal(1))),
            ("[0, .., 1]", list(Equal(0), null, Equal(1))),
            ("[..]", list((Pattern?)null)),
            ("[.. var rest]", list(slice)),
            ("[> 0, ..]", list(Compare(ComparisonKind.GreaterThan, 0), null)),
            ("[<= 0, _]", list(Compare(ComparisonKind.LessThanOrEqual, 0), Any(integer))),
            ("[_, > 0, ..]", list(Any(integer), Compare(ComparisonKind.GreaterThan, 0), null)),
            ("[.., 0]", list(null, Equal(0))),
            ("[not 1, ..]", list(new NegatedPattern(HandBuiltModel.Node(), integer, Equal(1)), null)),
            ("[0 or 1, _]", list(new DisjunctionPattern(HandBuiltModel.Node(), integer, [Equal(0), Equal(1)]), Any(integer))),
            ("int[] a", new TypeTestPattern(HandBuiltModel.Node(), numbers, null)),
            ("_", Any(numbers)),
            ("not [_, ..]", new NegatedPattern(HandBuiltModel.Node(), numbers, list(Any(integer), null))),
        ];
    }

    private List<(string Text, Pattern Pattern)> PropertyCatalogue(IMistType point, Property x, Property y)
    {
        var integer = h.Int;

        PropertyPattern property(params (Property Member, Pattern Pattern)[] subpatterns)
        {
            return new PropertyPattern(HandBuiltModel.Node(), point, [.. subpatterns.Select(subpattern
                => new PropertySubpattern(HandBuiltModel.Node(), subpattern.Member.Name, subpattern.Pattern, integer, new PropertyResult(subpattern.Member, point, [])))]);
        }

        return
        [
            ("{ X: 0 }", property((x, Equal(0)))),
            ("{ X: 1 }", property((x, Equal(1)))),
            ("{ X: >= 0 }", property((x, Compare(ComparisonKind.GreaterThanOrEqual, 0)))),
            ("{ X: > 0 }", property((x, Compare(ComparisonKind.GreaterThan, 0)))),
            ("{ X: < 1 }", property((x, Compare(ComparisonKind.LessThan, 1)))),
            ("{ X: <= 0 }", property((x, Compare(ComparisonKind.LessThanOrEqual, 0)))),
            ("{ Y: 1 }", property((y, Equal(1)))),
            ("{ X: 0, Y: 1 }", property((x, Equal(0)), (y, Equal(1)))),
            ("{ X: > 0, Y: <= 0 }", property((x, Compare(ComparisonKind.GreaterThan, 0)), (y, Compare(ComparisonKind.LessThanOrEqual, 0)))),
            ("{ X: _ }", property((x, Any(integer)))),
            ("{ }", property()),
            ("{ X: not 0 }", property((x, new NegatedPattern(HandBuiltModel.Node(), integer, Equal(0))))),
            ("{ X: 0 or 1 }", property((x, new DisjunctionPattern(HandBuiltModel.Node(), integer, [Equal(0), Equal(1)])))),
            ("{ X: > 0 and < 1 }", property((x, new ConjunctionPattern(HandBuiltModel.Node(), integer, [Compare(ComparisonKind.GreaterThan, 0), Compare(ComparisonKind.LessThan, 1)])))),
            ("Point p", new TypeTestPattern(HandBuiltModel.Node(), point, null)),
            ("not { X: 0 }", new NegatedPattern(HandBuiltModel.Node(), point, property((x, Equal(0))))),
            ("_", Any(point)),
        ];
    }

    private Property Member(MemberType owner, string name)
    {
        return new Property(name, h.Int, new MemberModifiers())
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
        };
    }

    private static CatchAllPattern Any(IMistType type)
    {
        return new CatchAllPattern(HandBuiltModel.Node(), type, null);
    }

    private ComparisonPattern Compare(ComparisonKind kind, int bound)
    {
        return h.Compare(h.Int, kind, bound);
    }

    private ComparisonPattern Equal(int bound)
    {
        return Compare(ComparisonKind.Equal, bound);
    }
}