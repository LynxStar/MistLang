namespace Mist.Common.Diagnostics;

/// <summary>
/// One CLI invocation's worth of reporting. It owns the ordinal counter, so an occurrence withheld
/// in one bag and replayed into another keeps the ordinal it was admitted with, and every bag the
/// driver creates for a compile counts in one sequence.
/// </summary>
public sealed class DiagnosticOperation
{
    private int admitted;

    public int Admitted => admitted;

    public DiagnosticPolicy Policy { get; set; } = DiagnosticPolicy.Default;

    internal int Next()
    {
        return Interlocked.Increment(ref admitted);
    }
}