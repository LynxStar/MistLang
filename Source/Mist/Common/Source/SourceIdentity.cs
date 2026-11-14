namespace Mist.Common.Source;

/// Path and content hash distinguish coincident ranges while allowing repeated loads of the same text.
public sealed class SourceIdentity : IEquatable<SourceIdentity>
{
    public SourceIdentity(SourceText text)
    {
        Text = text;
    }

    public SourceText Text { get; }

    public string Path => Text.Path;

    public string ContentHash => Text.ContentHash;

    public bool Equals(SourceIdentity? other)
    {
        return other is not null && Path == other.Path && ContentHash == other.ContentHash;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SourceIdentity);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Path, ContentHash);
    }

    public override string ToString()
    {
        return Path;
    }
}