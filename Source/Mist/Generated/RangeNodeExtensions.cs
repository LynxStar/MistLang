namespace Mist.Generated;

public static class RangeNodeExtensions
{
    /// <summary>
    /// The operand of a <see cref="NonRangeRangeNode"/>, or null for a <see cref="RangeExpressionRangeNode"/>: a
    /// precedence-chain walker looking through a bare operand treats a range as a non-match, and code that handles
    /// ranges matches the range form itself.
    /// </summary>
    public static UnaryNode? UnwrapRange(this RangeNode node)
    {
        return node is NonRangeRangeNode nonRange ? nonRange.Operand : null;
    }
}