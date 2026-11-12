using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

public abstract record ReferenceClassification
{
    private protected ReferenceClassification()
    {
    }
}

public sealed record ReferenceClassified(ReferenceKind Kind, string ResolvedPath) : ReferenceClassification;

public sealed record ReferenceNoCandidate : ReferenceClassification;

public sealed record ReferenceAmbiguous(ImmutableArray<string> Candidates) : ReferenceClassification;

public sealed record ReferenceUnsupportedExtension(string Extension) : ReferenceClassification;