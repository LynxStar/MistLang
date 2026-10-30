namespace Mist.Common.Diagnostics;

/// <summary>
/// A number that was issued and then retired. It is recorded so that it is never issued again: a
/// reader of old output or old documents must always find the one condition the number meant.
/// </summary>
public sealed record Tombstone(DiagnosticNumber Number, string Title, string RetiredAt);