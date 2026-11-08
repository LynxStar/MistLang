using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.Tests.Helpers;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

internal sealed class FactModel
{
    public HandBuiltRefinements Expressions { get; } = new();
    public static readonly Budget Limit = new(TimeSpan.FromMinutes(1), Budget.Default.DerivedFacts);

    public Value Value(string name)
    {
        return new Value(Expressions.Int, name, HandBuiltModel.Node());
    }

    public static Term Term(long constant, params (Value Value, long Coefficient)[] coefficients)
    {
        return new Term(coefficients.Select(pair => KeyValuePair.Create(pair.Value, new BigInteger(pair.Coefficient))), constant);
    }

    public static Comparison Le(long constant, params (Value Value, long Coefficient)[] coefficients)
    {
        return new Comparison(Term(constant, coefficients));
    }

    public static Comparison Eq(long constant, params (Value Value, long Coefficient)[] coefficients)
    {
        return new Comparison(Term(constant, coefficients), Relation.Equal);
    }

    public static Bag MakeBag(params Comparison[] facts)
    {
        return new Bag(facts, [Alternative.Empty]);
    }

    public static Proof Prove(Bag bag, Comparison denial, params Value[] contract)
    {
        return Chain.Prove(bag, [new Alternative([denial])], contract.ToHashSet(), Limit);
    }
}