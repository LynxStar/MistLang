using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Proves subset relationships between refinement predicates.
/// Used by the type checker to determine if one refinement type is assignable
/// to another (i.e., the source predicate logically implies the target predicate).
/// </summary>
/// <remarks>
/// The actual implication logic lives in <see cref="RefinementPredicate.Implies"/>.
/// This class provides a focused entry point for the type checker and can be
/// extended with additional proving strategies in the future.
/// </remarks>
internal static class RefinementSolver
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="source"/> logically implies
    /// <paramref name="target"/>. When this holds, any value satisfying the
    /// source predicate also satisfies the target predicate, making assignment
    /// from the source refinement type to the target refinement type safe.
    /// </summary>
    /// <remarks>
    /// Handles the following relational implications:
    /// <list type="bullet">
    ///   <item><c>&gt;= a</c> implies <c>&gt;= b</c> when <c>a &gt;= b</c></item>
    ///   <item><c>&lt;= a</c> implies <c>&lt;= b</c> when <c>a &lt;= b</c></item>
    ///   <item><c>&gt; a</c> implies <c>&gt;= b</c> when <c>a &gt;= b</c></item>
    ///   <item><c>&lt; a</c> implies <c>&lt;= b</c> when <c>a &lt;= b</c></item>
    ///   <item>Compound: <c>(&gt;= a and &lt;= b)</c> implies <c>(&gt;= c and &lt;= d)</c>
    ///         when <c>a &gt;= c</c> and <c>b &lt;= d</c></item>
    /// </list>
    /// </remarks>
    public static bool Implies(RefinementPredicate source, RefinementPredicate target)
    {
        return source.Implies(target);
    }
}