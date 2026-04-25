namespace Mist.Common.SourceText;

/// <summary>
/// A span of source text expressed as start and end line/column positions.
/// </summary>
public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End);
