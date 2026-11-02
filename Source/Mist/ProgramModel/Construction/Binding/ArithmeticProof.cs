using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The three-way rule for integer division and remainder (exceptions Requirement 4): proven safe takes
/// the numeric value, proven invalid is refused, and anything unproven takes the fallible Result route.
/// The proof is read off the operands' binding-time facts -- a constant, a declared refinement, or a
/// sound flow narrowing -- never off the destination type, so an expected <c>int</c> does not force
/// safety. The decision is made at binding, where it fixes the expression's type, because Stage 5 only
/// validates a finished model. Int32 is the family carrying a checked helper today; the narrow integers
/// promote into it. The wider, unsigned, decimal and floating-point families are left unconditional:
/// float and double hold IEEE non-finite values rather than errors, and decimal overflow needs the CLR
/// exception boundary.
/// </summary>
internal static class ArithmeticProof
{
    internal enum Verdict
    {
        Safe,
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

        // A remainder has no quotient to overflow: the signed minimum remainder negative one is defined
        // to be zero, which the CLR's own rem already yields without trapping, so a nonzero divisor is
        // the whole of its safety.
        if(op == BinaryOperatorKind.Modulo)
        {
            return zeroOnly ? Verdict.Invalid : zeroPossible ? Verdict.Fallible : Verdict.Safe;
        }

        var overflowPossible = left.Contains(int.MinValue) && right.Contains(-1);
        var overflowCertain = left.IsSubsetOf(Admitted.Exactly(int.MinValue)) && right.IsSubsetOf(Admitted.Exactly(-1));

        if(zeroOnly || overflowCertain)
        {
            return Verdict.Invalid;
        }

        return !zeroPossible && !overflowPossible ? Verdict.Safe : Verdict.Fallible;
    }
}