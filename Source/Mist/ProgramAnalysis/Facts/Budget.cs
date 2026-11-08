using System.Diagnostics;

namespace Mist.ProgramAnalysis.Facts;

internal sealed record Budget(TimeSpan Time, int DerivedFacts)
{
    public static readonly Budget Default = new(TimeSpan.FromMilliseconds(50), 20_000);
}

internal sealed class ProofBudget(Budget limit)
{
    private readonly long started = Stopwatch.GetTimestamp();
    private int derived;

    public bool Expired => derived > limit.DerivedFacts || Stopwatch.GetElapsedTime(started) > limit.Time;

    public bool Derive()
    {
        derived++;
        return !Expired;
    }
}