using FsCheck.Xunit;
using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class ChainSoundnessTests
{
    [Property(MaxTest = 50)]
    public bool ProvenNeverContradictsABruteForcewitness(int seed)
    {
        var random = new Random(seed);
        var model = new FactModel();
        var values = Enumerable.Range(0, random.Next(1, 5)).Select(index => model.Value($"x{index}")).ToArray();
        var facts = values.Select(value => Comparison.In(value, new IntegralSet([new(0, 7)]))).ToList();
        var constraints = new List<(Term Term, Relation Relation)>();
        var count = random.Next(0, 6 - values.Length);
        for(var index = 0; index < count; index++)
        {
            var drawn = Draw(random, values);
            constraints.Add(drawn);
            facts.Add(new Comparison(drawn.Term, drawn.Relation));
        }

        var denial = Draw(random, values);
        constraints.Add(denial);
        var proof = Chain.Prove(MakeBag([.. facts]), [new Alternative([new Comparison(denial.Term, denial.Relation)])], values.ToHashSet(), Limit);
        if(proof is not Proven)
        {
            return true;
        }

        var assignment = new Dictionary<Value, int>();
        return !witness(0);

        bool witness(int position)
        {
            if(position == values.Length)
            {
                return constraints.All(fact => Satisfied(fact.Term, fact.Relation, assignment));
            }

            for(var value = 0; value <= 7; value++)
            {
                assignment[values[position]] = value;
                if(witness(position + 1))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static (Term Term, Relation Relation) Draw(Random random, Value[] values)
    {
        var coefficients = values.Select(value => (value, (long)random.Next(-3, 4))).ToArray();
        var term = Term(random.Next(-8, 9), coefficients);
        return (term, random.Next(3) == 0 ? Relation.Equal : Relation.LessThanOrEqual);
    }

    private static bool Satisfied(Term term, Relation relation, Dictionary<Value, int> assignment)
    {
        var result = term.Coefficients.Aggregate(term.Constant, (sum, pair) => sum + pair.Value * assignment[pair.Key]);
        return relation == Relation.Equal ? result.IsZero : result.Sign <= 0;
    }
}