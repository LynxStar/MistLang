namespace Audit.Models;

public sealed record FileEntry(
    string RelativePath,
    AuditStatus Status,
    FileMetrics Metrics,
    IReadOnlyList<Finding> Findings,
    string? FailureReason);