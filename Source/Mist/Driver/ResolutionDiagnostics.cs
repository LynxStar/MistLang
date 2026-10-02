using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// What Stage 1.5 reported, held out of the bag whose errors stop the build. A dependency that
/// failed to resolve leaves the ASTs describing the program and every other assembly in place, so
/// its error is preserved in the result without failing it (csharp-interop-v2 Requirement 2.6): a
/// program that needed the dependency fails on the name Stage 4 cannot resolve, and one that did
/// not builds. <paramref name="Preceding"/> is how many diagnostics the stages before 1.5 reported,
/// which is where these go back for the result to read in stage order.
/// </summary>
internal sealed record ResolutionDiagnostics(ImmutableArray<Diagnostic> Reported, int Preceding)
{
    public CompilerResult JoinInto(CompilerResult result)
    {
        var position = Math.Min(Preceding, result.Diagnostics.Length);

        return result with { Diagnostics = result.Diagnostics.InsertRange(position, Reported) };
    }
}