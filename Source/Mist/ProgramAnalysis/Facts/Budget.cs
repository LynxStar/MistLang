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
    private int checks;
    private bool timedOut;

    // Sampling the clock bounds small proofs by their fact work rather than a scheduler pause.
    public bool Expired
    {
        get
        {
            if(derived > limit.DerivedFacts || limit.Time <= TimeSpan.Zero)
            {
                return true;
            }

            if(!timedOut && (++checks & 255) == 0)
            {
                timedOut = Stopwatch.GetElapsedTime(started) > limit.Time;
            }

            return timedOut;
        }
    }

    public bool Derive()
    {
        derived++;
        return !Expired;
    }
}