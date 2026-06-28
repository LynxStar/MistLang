namespace Audit.Models;

public sealed record Finding(
    FindingCategory Category,
    string Note,
    IReadOnlyList<string> Targets,
    FindingStatus Status);