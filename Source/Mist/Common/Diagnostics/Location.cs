using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// Source positions derive line/column from text; containment uses file identity and offsets,
/// with zero-width points and reason-matched absences.
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