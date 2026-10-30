using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// What the factory hands a bag: the occurrence a producer asked for and, when its arguments or
/// its span broke the descriptor's contract, the internal compiler errors that say so. All are
/// admitted, so the user still sees the real finding and the compiler's own mistake stays visible.
/// </summary>
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