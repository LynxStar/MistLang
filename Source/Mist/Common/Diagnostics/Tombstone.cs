namespace Mist.Common.Diagnostics;

/// Retired numbers stay reserved so old output and documents always identify the same condition.
public sealed record Tombstone(DiagnosticNumber Number, string Title, string RetiredAt);