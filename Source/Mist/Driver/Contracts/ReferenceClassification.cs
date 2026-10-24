using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>The outcome of deciding a reference's kind from its path alone or from a directory's own file names.</summary>
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