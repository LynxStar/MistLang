using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// Where an occurrence points, as a closed union: a position that exists is exact, and one that
/// does not says why it is absent. Offsets are authoritative; line and column are derived on
/// demand from the text the range was computed against, so nothing stores them and nothing can
/// fabricate them. Containment compares file identity together with the range, never line and
/// column alone, with a point counted as a zero-width range; two absent positions are held to
/// contain each other only when their reasons agree, which is what a hand-built model with no
/// file relies on.
/// </summary>
public abstract record Location
{
    private protected Location()
    {
    }

    public virtual string? Path => null;

    public virtual LinePositionSpan? Lines => null;

    public bool Contains(Location inner)
    {
        if(Extent(this) is not { } outer || Extent(inner) is not { } within)
        {
            return Equals(inner);
        }

        return outer.File.Equals(within.File) && outer.Start <= within.Start && within.End <= outer.End;
    }

    private static (SourceIdentity File, long Start, long End)? Extent(Location location)
    {
        return location switch
        {
            SourceRange range => (range.File, range.Offset, (long)range.Offset + range.Length),
            SourcePoint point => (point.File, point.Offset, point.Offset),
            _ => null,
        };
    }
}

public sealed record SourceRange(SourceIdentity File, int Offset, int Length) : Location
{
    public override string Path => File.Path;

    public override LinePositionSpan? Lines => File.Text.ToLinePositionSpan(new SourceSpan(Offset, Length));

    public int End => Offset + Length;
}

public sealed record SourcePoint(SourceIdentity File, int Offset) : Location
{
    public override string Path => File.Path;

    public override LinePositionSpan? Lines => File.Text.ToLinePositionSpan(new SourceSpan(Offset, 0));
}

public sealed record FileOnly(string Path) : Location
{
    public override string Path { get; } = Path;
}

public sealed record ProjectLocation(string ManifestPath, SourceRange? KeySpan) : Location
{
    public override string Path => ManifestPath;

    public override LinePositionSpan? Lines => KeySpan?.Lines;
}

public sealed record ExternalLocation(string Assembly, string Symbol) : Location;

public sealed record GeneratedLocation(string GeneratedPath, LinePositionSpan Native, SourceRange? Mapped) : Location
{
    public override string Path => GeneratedPath;

    public override LinePositionSpan? Lines => Native;
}

public sealed record Absent(AbsenceReason Reason) : Location;

public enum AbsenceReason
{
    NotYetAttached,
    TestOrigin,
    SyntheticWithoutRoot,
    OutOfBounds,
    Unavailable,
}