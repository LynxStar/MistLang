namespace Mist.Common.Diagnostics;

/// All bags in one CLI invocation share an ordinal sequence; replay preserves an occurrence's ordinal.
public sealed class DiagnosticOperation
{
    private int admitted;

    public int Admitted => admitted;

    public DiagnosticPolicy Policy { get; set; } = DiagnosticPolicy.Default;

    public DiagnosticPolicyOverrides Overrides { get; set; } = new();

    internal int Next()
    {
        return Interlocked.Increment(ref admitted);
    }
}