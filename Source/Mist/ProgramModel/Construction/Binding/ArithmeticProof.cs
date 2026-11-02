using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The three-way rule for integer division and remainder (exceptions Requirement 4): proven safe takes
/// the numeric value, proven invalid is refused, and anything unproven takes the fallible Result route.
/// The proof is read off the operands' binding-time facts -- a constant, a declared refinement, or a
/// sound flow narrowing -- never off the destination type, so an expected <c>int</c> does not force
/// safety. Signed remainder adds a fourth answer: the minimum-value remainder negative one is DEFINED to
/// be zero, so the operation keeps its numeric type, but the CLR's own <c>rem</c> traps on it, so a
/// nonzero divisor that cannot exclude that pair is proven safe yet guarded rather than emitted bare.
/// </summary>
internal static class ArithmeticProof
{
    internal enum Verdict
    {
        Safe,
        SafeGuardedRemainder,
        Invalid,
        Fallible,
    }

    public static Verdict? Judge(BinaryOperatorKind op, IMistType numericType, Admitted left, Admitted right)
    {
        if(op is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo)
            || numericType.FullyQualifiedName != WellKnownTypes.Int32.FullyQualifiedName)
        {
            return null;
        }

        if(left.Intervals.Length == 0 || right.Intervals.Length == 0)
        {
            return Verdict.Fallible;
        }

        var zeroPossible = right.Contains(0);
        var zeroOnly = right.IsSubsetOf(Admitted.Exactly(0));
        var minusOnePossible = right.Contains(-1);
        var minimumPossible = left.Contains(int.MinValue);

        if(op == BinaryOperatorKind.Modulo)
        {
            if(zeroOnly)
            {
                return Verdict.Invalid;
            }

            return zeroPossible ? Verdict.Fallible
                : minimumPossible && minusOnePossible ? Verdict.SafeGuardedRemainder
                : Verdict.Safe;
        }

        var overflowPossible = minimumPossible && minusOnePossible;
        var overflowCertain = left.IsSubsetOf(Admitted.Exactly(int.MinValue)) && right.IsSubsetOf(Admitted.Exactly(-1));

        if(zeroOnly || overflowCertain)
        {
            return Verdict.Invalid;
        }

        return !zeroPossible && !overflowPossible ? Verdict.Safe : Verdict.Fallible;
    }
}
