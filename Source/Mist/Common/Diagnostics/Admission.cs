namespace Mist.Common.Diagnostics;

/// <summary>
/// What the factory hands a bag: the occurrence a producer asked for and, when its arguments or
/// its span broke the descriptor's contract, the one internal compiler error that says so. Both
/// are admitted, so the user still sees the real finding and the compiler's own mistake stays visible.
/// </summary>
public readonly record struct Admission(Diagnostic Occurrence, Diagnostic? Breach)
{
    public IEnumerable<Diagnostic> Occurrences
    {
        get
        {
            yield return Occurrence;
            if(Breach is not null)
            {
                yield return Breach;
            }
        }
    }
}