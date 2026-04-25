namespace Mist.Common.SourceText;

/// <summary>
/// A span of source text expressed as a start offset and length.
/// </summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    /// <summary>
    /// The exclusive end offset of this span.
    /// </summary>
    public int End => Start + Length;
}
