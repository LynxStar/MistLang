using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

namespace Mist.ProgramAnalysis.Facts;

internal enum Relation
{
    Equal,
    LessThanOrEqual,
    LessThan,
}

internal sealed record Fact
{
    public Fact(Term term, Relation relation = Relation.LessThanOrEqual, bool merged = false)
    {
        IsReal = term.Variables.Any(value => value.IsReal);
        var divisor = term.Multipliers.Aggregate(IsReal ? BigInteger.Abs(term.Constant) : BigInteger.Zero,
            (gcd, coefficient) => BigInteger.GreatestCommonDivisor(gcd, BigInteger.Abs(coefficient)));
        if(divisor > 1)
        {
            if(IsReal)
            {
                term = new Term(term.Variables.Zip(term.Multipliers, (value, multiplier) => KeyValuePair.Create(value, multiplier / divisor)), term.Constant / divisor);
            }
            else if(relation == Relation.Equal && term.Constant % divisor != 0)
            {
                term = new Term(1);
            }
            else if(relation != Relation.LessThan)
            {
                var constant = BigInteger.DivRem(term.Constant, divisor, out var remainder);
                term = new Term(term.Variables.Zip(term.Multipliers, (value, multiplier) => KeyValuePair.Create(value, multiplier / divisor)),
                    constant + (relation == Relation.LessThanOrEqual && remainder > 0 ? 1 : 0));
            }
        }

        if(relation == Relation.Equal && term.Variables.Length > 0 && term.Multipliers[0].Sign < 0)
        {
            term = term.Scale(-1);
        }

        Term = term;
        Relation = relation;
        IsMerged = merged;
    }

    private Fact(Value value, IntegralSet values, bool merged)
    {
        Term = new Term(value);
        Relation = Relation.Equal;
        Subject = value;
        Values = values;
        IsMerged = merged;
    }

    public Term Term { get; }
    public Relation Relation { get; }
    public bool IsReal { get; }
    public bool IsMerged { get; init; }
    public Value? Subject { get; }
    public IntegralSet? Values { get; }

    public bool IsContradiction => Values is { } values ? values.IsEmpty : Term.Variables.IsEmpty && Relation switch
    {
        Relation.Equal => !Term.Constant.IsZero,
        Relation.LessThanOrEqual => Term.Constant.Sign > 0,
        _ => Term.Constant.Sign >= 0,
    };

    public static Fact In(Value value, IntegralSet values, bool merged = false)
    {
        return new Fact(value, values, merged);
    }

    public ImmutableArray<Alternative> Denial()
    {
        if(Values is not null || (Relation == Relation.LessThan && !IsReal && !Term.Variables.IsEmpty))
        {
            throw new InvalidOperationException("Denial requires an integral comparison.");
        }

        if(IsReal || Relation == Relation.LessThan)
        {
            return Relation switch
            {
                Relation.Equal => [new Alternative([new Fact(Term, Relation.LessThan)]), new Alternative([new Fact(Term.Scale(-1), Relation.LessThan)])],
                Relation.LessThan => [new Alternative([new Fact(Term.Scale(-1))])],
                _ => [new Alternative([new Fact(Term.Scale(-1), Relation.LessThan)])],
            };
        }

        return Relation == Relation.Equal
            ? [new Alternative([new Fact(Term.Add(new Term(1)))]), new Alternative([new Fact(Term.Scale(-1).Add(new Term(1)))])]
            : [new Alternative([new Fact(Term.Scale(-1).Add(new Term(1)))])];
    }

    public override string ToString()
    {
        if(Subject is { } subject && Values is { } values)
        {
            return $"{subject.DisplayName} in ({string.Join(" or ", values.Intervals.Select(interval => $"[{interval.Low}, {interval.High}]"))})";
        }

        var symbol = Relation switch { Relation.Equal => "==", Relation.LessThan => "<", _ => "<=" };
        if(Term.Coefficients.Count == 1 && Term.Coefficients.First() is var pair && BigInteger.Abs(pair.Value).IsOne)
        {
            if(pair.Value.Sign < 0)
            {
                symbol = Relation switch { Relation.Equal => "==", Relation.LessThan => ">", _ => ">=" };
            }

            return string.Create(CultureInfo.InvariantCulture, $"{pair.Key.DisplayName} {symbol} {-Term.Constant / pair.Value}");
        }

        return $"{Term} {symbol} 0";
    }
}