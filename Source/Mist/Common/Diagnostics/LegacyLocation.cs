namespace Mist.Common.Diagnostics;

/// A span without its source text cannot be placed safely, so this adapter preserves only the path.
internal static class LegacyLocation
{
    public static Location Map(string? path)
    {
        return path is null or "" or "<unknown>" ? new Absent(AbsenceReason.Unavailable) : new FileOnly(path);
    }
}