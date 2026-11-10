using System.Globalization;
using System.Numerics;
using System.Text;
using Mist.ProgramAnalysis.Refinements;

namespace Mist.ProgramAnalysis.Facts;

internal static class FailingCase
{
    public static string? Describe(Refuted refuted)
    {
        var shown = Spelled(Narrowed(refuted.FailingCase.Facts));
        return shown.Count == 0 ? null : $"fails when {string.Join(" and ", shown)}{(refuted.AfterMerge ? ", after merging paths" : "")}";
    }

    // The chain stops at the contract's values; a reader wants the statement those values are left with.
    // So a further value is removed only while removing it states something the untouched facts did not,
    // which turns `used <= capacity and used >= 2 * capacity + 1` into `capacity <= -1` and leaves
    // `start == items.Length` alone. A type's own range is never the reason a use fails, so it is set aside.
    internal static List<Fact> Narrowed(IEnumerable<Fact> source)
    {
        var meter = new ProofBudget(Budget.Default);
        var facts = Tightest(Chain.Clean(source.Where(fact => !Typical(fact))));
        while(Narrower(facts, meter) is { } narrower)
        {
            facts = narrower;
        }

        return facts;
    }

    private static List<Fact> Tightest(List<Fact> facts)
    {
        return [.. facts.Where(fact => fact.Relation != Relation.LessThanOrEqual || !facts.Any(other => other.Relation == Relation.LessThanOrEqual
            && other.Term.Constant > fact.Term.Constant && other.Term.Variables.SequenceEqual(fact.Term.Variables)
            && other.Term.Multipliers.SequenceEqual(fact.Term.Multipliers)))];
    }

    internal static List<string> Spelled(IReadOnlyList<Fact> facts)
    {
        var shown = new List<string>();
        foreach(var fact in facts.Where(fact => fact.Term.Variables.Length > 1))
        {
            var opposite = fact.Term.Scale(-1);
            if(fact.Relation == Relation.LessThanOrEqual && facts.Any(other => other.Relation == Relation.LessThanOrEqual && other.Term.Equals(opposite)))
            {
                if(fact.Term.Multipliers[0].Sign > 0)
                {
                    shown.Add(Compared(fact.Term, "=="));
                }

                continue;
            }

            shown.Add(Compared(fact.Term, fact.Relation switch { Relation.Equal => "==", Relation.LessThan => "<", _ => "<=" }));
        }

        foreach(var value in facts.Where(fact => fact.Term.Variables.Length == 1).Select(fact => fact.Term.Variables[0]).Distinct())
        {
            if(value.IsReal)
            {
                shown.AddRange(facts.Where(fact => fact.Values is null && fact.Term.Variables is [var only] && only == value).Select(Bounded));
                continue;
            }

            var known = Chain.SingleValue(facts.Where(fact => fact.Term.Variables.Length == 1), value);
            if(Interval(value, known) is { Length: > 0 } interval)
            {
                shown.Add(interval);
            }
        }

        return shown;
    }

    private static List<Fact>? Narrower(List<Fact> facts, ProofBudget meter)
    {
        var mentions = facts.SelectMany(fact => fact.Term.Variables).GroupBy(value => value)
            .OrderBy(group => group.Count()).ThenBy(group => group.Key.Id).Select(group => group.Key);
        foreach(var value in mentions)
        {
            if(Chain.Eliminate(facts, value, meter) is not { } next || next.Any(fact => fact.IsContradiction))
            {
                if(meter.Expired)
                {
                    return null;
                }

                continue;
            }

            next = Tightest([.. next.Where(fact => !Typical(fact))]);
            if(Adds(next, [.. facts.Where(fact => !fact.Term.Variables.Contains(value))]))
            {
                return next;
            }
        }

        return null;
    }

    private static bool Adds(List<Fact> next, List<Fact> untouched)
    {
        return next.Any(fact => fact.Term.Variables switch
        {
            [] => false,
            [var only] when only.IsReal => !untouched.Contains(fact),
            [var only] => Chain.SingleValue(untouched, only) is var known && !known.And(Chain.SingleValue([fact], only)).Equals(known),
            _ => !untouched.Contains(fact),
        });
    }

    private static bool Typical(Fact fact)
    {
        if(fact.Term.Variables is [{ IsReal: true } real])
        {
            return RealRequirement.OfType(real.Type) is { } declared && RealFacts.Within(declared, new Term(real)) is [var within] && within.Facts.Contains(fact);
        }

        return fact.Term.Variables is [var only] && fact.Values is null && Declared(only) is var range
            && range.And(Chain.SingleValue([fact], only)).Equals(range);
    }

    private static string Bounded(Fact fact)
    {
        var value = fact.Term.Variables[0];
        var multiplier = fact.Term.Multipliers[0];
        var bound = new Rational(-fact.Term.Constant * multiplier.Sign, BigInteger.Abs(multiplier));
        var relation = fact.Relation switch
        {
            Relation.Equal => "==",
            Relation.LessThan => multiplier.Sign > 0 ? "<" : ">",
            _ => multiplier.Sign > 0 ? "<=" : ">=",
        };
        if(RealRequirement.KindOf(value.Type) is RealKind.Single or RealKind.Double && relation is "<=" or ">=" && Binary(bound) is { } point)
        {
            var single = RealRequirement.KindOf(value.Type) == RealKind.Single;
            var beyond = relation == "<=" ? single ? float.BitIncrement((float)point) : double.BitIncrement(point)
                : single ? float.BitDecrement((float)point) : double.BitDecrement(point);
            if(double.IsFinite(beyond) && Number(beyond, single).Length < Number(point, single).Length)
            {
                return $"{value.DisplayName} {relation[..1]} {Number(beyond, single)}";
            }
        }

        return $"{value.DisplayName} {relation} {Shown(bound)}";
    }

    private static double? Binary(Rational bound)
    {
        var point = RealNumbers.IsPowerOfTwo(bound.Denominator) ? Math.ScaleB((double)bound.Numerator, -(int)(bound.Denominator.GetBitLength() - 1))
            : (double)bound.Numerator / (double)bound.Denominator;
        return double.IsFinite(point) && RealNumbers.Of(point) is { } exact && exact.Numerator * bound.Denominator == bound.Numerator * exact.Denominator ? point : null;
    }

    private static string Number(double point, bool single)
    {
        return point == 0 ? "0" : single ? ((float)point).ToString("R", CultureInfo.InvariantCulture) : point.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Shown(Rational bound)
    {
        if(Binary(bound) is { } point)
        {
            return Number(point, single: false);
        }

        var divisor = BigInteger.GreatestCommonDivisor(bound.Numerator, bound.Denominator);
        var (numerator, denominator) = divisor.IsZero || divisor.IsOne ? (bound.Numerator, bound.Denominator) : (bound.Numerator / divisor, bound.Denominator / divisor);
        if(denominator.IsOne)
        {
            return numerator.ToString(CultureInfo.InvariantCulture);
        }

        var places = 0;
        var scale = BigInteger.One;
        while(places < 28 && !(scale % denominator).IsZero)
        {
            (places, scale) = (places + 1, scale * 10);
        }

        if(!(scale % denominator).IsZero)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{numerator} / {denominator}");
        }

        var digits = BigInteger.Abs(numerator * (scale / denominator)).ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        return $"{(numerator.Sign < 0 ? "-" : "")}{digits[..^places]}.{digits[^places..]}";
    }

    private static IntegralSet Declared(Value value)
    {
        var range = IntegralTypes.Range(value.Type) is { } own ? new IntegralSet([own]) : IntegralSet.Everything;
        return Requirement.ValuesOf(value.Type) is { } admitted ? range.And(IntegralSet.Of(admitted)) : range;
    }

    private static string? Interval(Value value, IntegralSet known)
    {
        var declared = Declared(value);
        if(known.Equals(declared) || known.IsEmpty || declared.IsEmpty)
        {
            return null;
        }

        var low = known.Intervals[0].Low;
        var high = known.Intervals[^1].High;
        if(known.Intervals.Length > 1)
        {
            return $"({string.Join(" or ", known.Intervals.Select(part => Between(value, part.Low, part.High, null, null)))})";
        }

        return Between(value, low, high, declared.Intervals[0].Low, declared.Intervals[^1].High);
    }

    private static string Between(Value value, BigInteger? low, BigInteger? high, BigInteger? lowest, BigInteger? highest)
    {
        if(low is { } only && low == high)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{value.DisplayName} == {only}");
        }

        var parts = new List<string>();
        if(low is { } floor && low != lowest)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{value.DisplayName} >= {floor}"));
        }

        if(high is { } ceiling && high != highest)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{value.DisplayName} <= {ceiling}"));
        }

        return string.Join(" and ", parts);
    }

    private static string Compared(Term term, string relation)
    {
        var lead = term.Variables[0];
        var multiplier = term.Multipliers[0];
        var rest = new Term(term.Variables.Skip(1).Zip(term.Multipliers.Skip(1), KeyValuePair.Create), term.Constant);
        var flipped = multiplier.Sign < 0 && relation != "==";
        var right = multiplier.Sign < 0 ? rest : rest.Scale(-1);
        return $"{Sum(new Term([KeyValuePair.Create(lead, BigInteger.Abs(multiplier))]))} {(flipped ? relation.Replace('<', '>') : relation)} {Sum(right)}";
    }

    private static string Sum(Term term)
    {
        var text = new StringBuilder();
        for(var i = 0; i < term.Variables.Length; i++)
        {
            var multiplier = term.Multipliers[i];
            var magnitude = BigInteger.Abs(multiplier);
            var name = magnitude.IsOne ? term.Variables[i].DisplayName : string.Create(CultureInfo.InvariantCulture, $"{magnitude} * {term.Variables[i].DisplayName}");
            _ = text.Append(i == 0 ? multiplier.Sign < 0 ? "-" : "" : multiplier.Sign < 0 ? " - " : " + ").Append(name);
        }

        if(!term.Constant.IsZero || text.Length == 0)
        {
            var constant = BigInteger.Abs(term.Constant).ToString(CultureInfo.InvariantCulture);
            _ = text.Length == 0 ? text.Append(term.Constant.ToString(CultureInfo.InvariantCulture))
                : text.Append(term.Constant.Sign < 0 ? " - " : " + ").Append(constant);
        }

        return text.ToString();
    }
}