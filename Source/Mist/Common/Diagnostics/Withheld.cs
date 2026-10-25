using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// What a speculative step reported, held back for the caller to decide: commit all of it to the
/// parent with ordinals intact, discard it, or replay one occurrence the caller kept from an
/// earlier withhold. Nothing here removes an occurrence another one resembles.
/// </summary>
public sealed class Withheld
{
    private readonly DiagnosticBag parent;

    internal Withheld(DiagnosticBag parent, ImmutableArray<Diagnostic> occurrences)
    {
        this.parent = parent;
        Occurrences = occurrences;
    }

    public ImmutableArray<Diagnostic> Occurrences { get; }

    public bool Decided { get; private set; }

    public void Commit()
    {
        foreach(var occurrence in Occurrences)
        {
            parent.Add(occurrence);
        }

        Decided = true;
    }

    public void Discard()
    {
        Decided = true;
    }

    public void Replay(Diagnostic earlier)
    {
        parent.Add(earlier);
    }
}