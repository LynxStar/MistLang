namespace Audit.Models;

public sealed record FileMetrics(int LineCount, IReadOnlyList<TypeInfo> Types);