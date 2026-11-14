using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// Contract breaches accompany the requested occurrence so both the user's finding and the compiler fault stay visible.
public readonly record struct Admission(Diagnostic Occurrence, ImmutableArray<Diagnostic> Breaches)
{
    public IEnumerable<Diagnostic> Occurrences
    {
        get
        {
            yield return Occurrence;
            foreach(var breach in Breaches.IsDefault ? [] : Breaches)
            {
                yield return breach;
            }
        }
    }
}