using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The three-way rule for integer division and remainder (exceptions Requirement 4): proven safe takes
/// the numeric value, proven invalid is refused, and anything unproven takes the fallible Result route.
/// The proof is read off the operands' binding-time facts -- a constant, a declared refinement, or a
/// sound flow narrowing -- never off the destination type, so an expected numeric destination does not
/// force safety. Every signed integer family shares one shape (a nonzero divisor that also excludes the
/// family minimum over negative one is safe), differing only in that minimum; unsigned families have no
/// overflow case. Signed remainder minimum over negative one is a defined zero, but the CLR rem traps on
/// it at the Int32 and Int64 widths (narrow families promote and compute it as zero), so that pair is
/// proven safe yet guarded rather than emitted bare where the backend would trap.
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
        // Decimal has no wrapping to fall back on: every add, subtract, multiply, divide and remainder
        // can overflow (and divide/remainder can hit a zero divisor), and the integer interval algebra
        // cannot reason about a decimal value, so none is provably safe -- each takes the fallible route
        // (exceptions Requirement 4), which checks the hazard at a Catch boundary.
        if(numericType.FullyQualifiedName == WellKnownTypes.Decimal.FullyQualifiedName)
        {
            return op is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply
                or BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo
                ? Verdict.Fallible
                : null;
        }

        if(op is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo)
            || Family(numericType) is not { } family)
        {
            return null;
        }

        if(left.Intervals.Length == 0 || right.Intervals.Length == 0)
        {
            return Verdict.Fallible;
        }

        var zeroPossible = right.Contains(0);
        var zeroOnly = right.IsSubsetOf(Admitted.Exactly(0));
        var minusOnePossible = family.Signed && right.Contains(-1);
        var minimumPossible = family.Signed && left.Contains(family.Minimum);

        if(op == BinaryOperatorKind.Modulo)
        {
            if(zeroOnly)
            {
                return Verdict.Invalid;
            }

            return zeroPossible ? Verdict.Fallible
                : family.RemainderTraps && minimumPossible && minusOnePossible ? Verdict.SafeGuardedRemainder
                : Verdict.Safe;
        }

        var overflowPossible = minimumPossible && minusOnePossible;
        var overflowCertain = family.Signed
            && left.IsSubsetOf(Admitted.Exactly(family.Minimum)) && right.IsSubsetOf(Admitted.Exactly(-1));

        if(zeroOnly || overflowCertain)
        {
            return Verdict.Invalid;
        }

        return !zeroPossible && !overflowPossible ? Verdict.Safe : Verdict.Fallible;
    }

    private readonly record struct IntegerFamily(bool Signed, long Minimum, bool RemainderTraps);

    private static IntegerFamily? Family(IMistType numericType)
    {
        var fqn = numericType.FullyQualifiedName;

        return fqn switch
        {
            // SByte and Int16 are absent on purpose: a narrow signed quotient minimum / -1 is
            // unrepresentable and cannot be expressed as a narrow checked helper, so those two families
            // drop their division/remainder operators and promote into Int32, judged there.
            _ when fqn == WellKnownTypes.Int32.FullyQualifiedName => new IntegerFamily(true, int.MinValue, true),
            _ when fqn == WellKnownTypes.Int64.FullyQualifiedName => new IntegerFamily(true, long.MinValue, true),
            _ when fqn == WellKnownTypes.Byte.FullyQualifiedName => new IntegerFamily(false, 0, false),
            _ when fqn == WellKnownTypes.UInt16.FullyQualifiedName => new IntegerFamily(false, 0, false),
            _ when fqn == WellKnownTypes.UInt32.FullyQualifiedName => new IntegerFamily(false, 0, false),
            _ when fqn == WellKnownTypes.UInt64.FullyQualifiedName => new IntegerFamily(false, 0, false),
            _ => null,
        };
    }
}