namespace Mist.Common.Source;

/// <summary>
/// Which text a span was computed against: the path and a hash of the contents, so two files with
/// coincident ranges never compare equal and a range is always read back against the text that
/// produced it, even when the same file was loaded twice.
/// </summary>
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