using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>A null result is a set this cannot state, never <see cref="Nothing"/>.</summary>
internal sealed record ValueAlgebra<T>(
    T Everything,
    T Nothing,
    Func<T, T, T?> And,
    Func<T, T, T?> Or,
    Func<T, T?> Not,
    Func<T, T, bool> IsSubsetOf,
    Func<Pattern, ValueAlgebra<T>, T?> Primary)
    where T : class
{
    public bool IsEmpty(T values)
    {
        return IsSubsetOf(values, Nothing);
    }

    public T? Read(Pattern pattern)
    {
        return pattern switch
        {
            CatchAllPattern => Everything,
            NegatedPattern negated => Read(negated.Operand) is { } inner ? Not(inner) : null,
            ConjunctionPattern conjunction => Fold(conjunction.Operands, Everything, And),
            DisjunctionPattern disjunction => Fold(disjunction.Operands, Nothing, Or),
            _ => Primary(pattern, this),
        };
    }

    public ValueAlgebra<object> Erased()
    {
        return new ValueAlgebra<object>(
            Everything, Nothing,
            (left, right) => And((T)left, (T)right), (left, right) => Or((T)left, (T)right), values => Not((T)values),
            (values, other) => IsSubsetOf((T)values, (T)other),
            (pattern, _) => Primary(pattern, this));
    }

    private T? Fold(IEnumerable<Pattern> operands, T seed, Func<T, T, T?> combine)
    {
        var result = seed;

        foreach(var operand in operands)
        {
            if(Read(operand) is not { } part || combine(result, part) is not { } combined)
            {
                return null;
            }

            result = combined;
        }

        return result;
    }
}