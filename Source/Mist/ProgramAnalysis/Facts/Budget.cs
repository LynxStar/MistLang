namespace Mist.ProgramAnalysis.Facts;

internal sealed record Budget(int DerivedFacts)
{
    public static readonly Budget Default = new(20_000);
}

internal sealed class ProofBudget(Budget limit)
{
    public int Derived { get; private set; }

    public bool Expired => limit.DerivedFacts <= 0 || Derived > limit.DerivedFacts;

    public bool Derive(int count = 1)
    {
        Derived += count;
        return !Expired;
    }
}