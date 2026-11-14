using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// Holds speculative reports for commit, discard or replay under <see cref="DiagnosticOperation"/> identity;
/// similar occurrences remain distinct.
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
        if(Decided)
        {
            throw new InvalidOperationException("The withheld occurrences were already committed or discarded.");
        }

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