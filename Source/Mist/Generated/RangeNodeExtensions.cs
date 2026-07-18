// Backward-compatible / dispatch helper for the Range precedence tier.
//
// The grammar inserted a new Range tier between Multiplicative and Unary:
//   Multiplicative = left:Range (operator:(STAR | SLASH | MODULO) right:Range)*
//   Range = start:Unary? DOTDOT end:Unary? as RangeExpression
//         | operand:Unary                  as NonRange
// which retyped MultiplicativeNode.Left/.Right from UnaryNode to the new abstract
// RangeNode base (with sealed subtypes RangeExpressionRangeNode and NonRangeRangeNode).
//
// The overwhelming majority of precedence-chain walkers only ever expect the
// pass-through (NonRange) shape -- a bare operand with no `..`. This helper lets
// those call sites reach the inner UnaryNode through a single dispatch point and
// treat a genuine range expression as "not the shape I am looking for" (a non-match).
// Code that genuinely handles ranges (semantic model, type checker, emitter, pretty
// printer) adds explicit RangeExpressionRangeNode handling instead.

namespace Mist.Generated;

/// <summary>
/// Dispatch helper on the abstract <see cref="RangeNode"/> introduced when the
/// <c>Range</c> precedence tier was inserted between <c>Multiplicative</c> and
/// <c>Unary</c>.
/// </summary>
public static class RangeNodeExtensions
{
    /// <summary>
    /// Returns the inner <see cref="UnaryNode"/> when this range node is the
    /// pass-through <see cref="NonRangeRangeNode"/> form, or <c>null</c> when it is a
    /// genuine <see cref="RangeExpressionRangeNode"/> (which a precedence-chain walker
    /// should treat as a non-match).
    /// </summary>
    public static UnaryNode? UnwrapRange(this RangeNode node)
    {
        return node is NonRangeRangeNode nonRange ? nonRange.Operand : null;
    }
}