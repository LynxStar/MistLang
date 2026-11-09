using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Rules;

internal static class ContractReader
{
    public static IReadOnlyList<Alternative>? Denial(IMistType target, Term value, Func<SymbolicBound, Term?> symbols)
    {
        return Requirement.OfType(target) is { } required ? Denial(required, value, symbols) : null;
    }

    public static IReadOnlyList<Alternative>? Denial(Requirement required, Term value, Func<SymbolicBound, Term?> symbols)
    {
        var denial = Intervals(required.Values.Not(), value).ToList();
        foreach(var bound in required.Bounds)
        {
            if(Limit(bound, symbols) is not { } limit)
            {
                return null;
            }

            denial.AddRange(Compare(value, bound.Kind, limit).Denial());
        }

        return denial;
    }

    public static Term? Limit(SymbolicBound bound, Func<SymbolicBound, Term?> symbols)
    {
        return bound.Operand is BinaryExpression or UnaryExpression
            ? Terms.ReadNamed(bound.Operand, name => Requirement.Symbol(name, bound.Kind) is { } named ? symbols(named) : null)
            : symbols(bound);
    }

    public static IEnumerable<Alternative> Intervals(Admitted values, Term value)
    {
        foreach(var interval in values.Intervals)
        {
            var facts = new List<Fact>();
            if(interval.Low != long.MinValue)
            {
                facts.Add(new Fact(value.Scale(-1).Add(new Term(interval.Low))));
            }

            if(interval.High != long.MaxValue)
            {
                facts.Add(new Fact(value.Add(new Term(-(System.Numerics.BigInteger)interval.High))));
            }

            yield return new Alternative(facts);
        }
    }

    public static Fact Compare(Term value, ComparisonKind kind, Term bound)
    {
        var difference = value.Subtract(bound);
        return kind switch
        {
            ComparisonKind.Equal => new Fact(difference, Relation.Equal),
            ComparisonKind.LessThan => new Fact(difference.Add(new Term(1))),
            ComparisonKind.LessThanOrEqual => new Fact(difference),
            ComparisonKind.GreaterThan => new Fact(difference.Scale(-1).Add(new Term(1))),
            _ => new Fact(difference.Scale(-1)),
        };
    }
}