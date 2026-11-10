namespace Mist.ProgramAnalysis.Facts;

internal static class Booleans
{
    public static IEnumerable<Alternative> Initializer(Value flag, Fact condition, bool positive, IReadOnlyList<Alternative> denial, Fact? witness = null)
    {
        var yes = new Fact(new Term(flag).Subtract(new Term(positive ? 1 : 0)), Relation.Equal);
        var no = new Fact(new Term(flag).Subtract(new Term(positive ? 0 : 1)), Relation.Equal);
        return new[] { new Alternative(witness is null ? [yes, condition] : [yes, condition, witness]) }.Concat(denial.Select(alternative => alternative.Add([no])));
    }
}