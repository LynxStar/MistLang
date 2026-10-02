using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// What Stage 1.5 reported, held out of the bag whose errors stop the build at its entry. A
/// dependency that failed to resolve leaves the ASTs describing the program and every other
/// assembly in place, so lexing, parsing, binding and analysis still run and every diagnostic they
/// find is reported beside it (csharp-interop-v2 Requirements 2.3 and 2.6); the pipeline reads the
/// bag again at its emission gate, where an error fails the build. <paramref name="Preceding"/> is
/// how many diagnostics the stages before 1.5 reported, which is where these go back for the result
/// to read in stage order.
/// </summary>
internal sealed record ResolutionDiagnostics(ImmutableArray<Diagnostic> Reported, int Preceding)
{
    public CompilerResult JoinInto(CompilerResult result)
    {
        var position = Math.Min(Preceding, result.Diagnostics.Length);

        return result with { Diagnostics = result.Diagnostics.InsertRange(position, Reported) };
    }
}