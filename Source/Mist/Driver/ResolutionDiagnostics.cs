using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Kept out of <see cref="PipelineInput.Diagnostics"/>, whose errors stop the pipeline at its
/// entry: a failed dependency leaves the ASTs describing the program, so binding and analysis must
/// still run (csharp-interop-v2 Requirements 2.3 and 2.6).
/// </summary>
internal sealed class ResolutionDiagnostics(int preceding, IResolutionDiagnosticListener? listener)
{
    private int delivered;

    public DiagnosticBag Reported { get; } = new();

    public void Deliver()
    {
        var reported = Reported.ToImmutableArray();
        for(; delivered < reported.Length; delivered++)
        {
            listener?.Receive(reported[delivered]);
        }
    }

    public CompilerResult JoinInto(CompilerResult result)
    {
        var position = Math.Min(preceding, result.Diagnostics.Length);

        return result with { Diagnostics = result.Diagnostics.InsertRange(position, Reported.ToImmutableArray()) };
    }
}