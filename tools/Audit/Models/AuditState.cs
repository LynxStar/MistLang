namespace Audit.Models;

public sealed record AuditState(IReadOnlyList<FileEntry> Files);