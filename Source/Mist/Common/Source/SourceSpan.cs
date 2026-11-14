namespace Mist.Common.Source;

public readonly record struct SourceSpan(int Start, int Length)
{
    /// The end offset is exclusive.
    public int End => Start + Length;
}