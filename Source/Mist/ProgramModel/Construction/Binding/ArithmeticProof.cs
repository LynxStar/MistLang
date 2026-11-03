using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

// Standard's named operand refinements define the hazards; binding fixes the type before dependent lookup.
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
            || Contract(numericType, "Nonzero") is not { } nonzero)
        {
            return null;
        }

        if(left.Intervals.Length == 0 || right.Intervals.Length == 0)
        {
            return Verdict.Fallible;
        }

        if(right.IsSubsetOf(nonzero.Not()))
        {
            return Verdict.Invalid;
        }

        var noOverflow = Contract(numericType, "QuotientDividend") is not { } dividend
            || Contract(numericType, "QuotientDivisor") is not { } divisor
            || left.IsSubsetOf(dividend) || right.IsSubsetOf(divisor);

        if(op == BinaryOperatorKind.Modulo)
        {
            return !right.IsSubsetOf(nonzero) ? Verdict.Fallible
                : noOverflow ? Verdict.Safe : Verdict.SafeGuardedRemainder;
        }

        if(Contract(numericType, "QuotientDividend") is { } validLeft
            && Contract(numericType, "QuotientDivisor") is { } validRight
            && left.IsSubsetOf(validLeft.Not()) && right.IsSubsetOf(validRight.Not()))
        {
            return Verdict.Invalid;
        }

        return right.IsSubsetOf(nonzero) && noOverflow ? Verdict.Safe : Verdict.Fallible;
    }

    private static Admitted? Contract(IMistType numeric, string name)
    {
        return numeric.NestedTypes.FirstOrDefault(type => type.Name == name) is { } declared
            ? Requirement.ValuesOf(declared) : null;
    }
}