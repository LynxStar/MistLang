namespace Mist.Common.Diagnostics;

/// <summary>
/// Maps the path and line span a Phase 2 producer still passes onto the union. A span without
/// the text it was computed against cannot be placed, so only its path survives; the two Storm
/// adapters and the node extension, the only producers that have a real position, build a
/// location of their own instead of coming through here.
/// </summary>
internal static class LegacyLocation
{
    public static Location Map(string? path)
    {
        return path is null or "" or "<unknown>" ? new Absent(AbsenceReason.Unavailable) : new FileOnly(path);
    }
}