using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

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

    public static Verdict? Judge(BinaryOperatorKind op, IMistType numericType, Admitted left, Admitted right,
        Int128? leftConstant = null, Int128? rightConstant = null)
    {
        if(op is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo)
            || Contract(numericType, "Nonzero") is null)
        {
            return null;
        }

        if(left.Intervals.Length == 0 || right.Intervals.Length == 0)
        {
            return Verdict.Fallible;
        }

        if(Satisfies(numericType, "Nonzero", right, rightConstant, negated: true))
        {
            return Verdict.Invalid;
        }

        var noOverflow = Contract(numericType, "QuotientDividend") is null
            || Contract(numericType, "QuotientDivisor") is null
            || Satisfies(numericType, "QuotientDividend", left, leftConstant) || Satisfies(numericType, "QuotientDivisor", right, rightConstant);

        if(op == BinaryOperatorKind.Modulo)
        {
            return !Satisfies(numericType, "Nonzero", right, rightConstant) ? Verdict.Fallible
                : noOverflow ? Verdict.Safe : Verdict.SafeGuardedRemainder;
        }

        if(Contract(numericType, "QuotientDividend") is not null
            && Contract(numericType, "QuotientDivisor") is not null
            && Satisfies(numericType, "QuotientDividend", left, leftConstant, negated: true)
            && Satisfies(numericType, "QuotientDivisor", right, rightConstant, negated: true))
        {
            return Verdict.Invalid;
        }

        return Satisfies(numericType, "Nonzero", right, rightConstant) && noOverflow ? Verdict.Safe : Verdict.Fallible;
    }

    private static bool Satisfies(IMistType numeric, string name, Admitted values, Int128? constant, bool negated = false)
    {
        if(constant is { } exact && (exact < long.MinValue || exact > long.MaxValue))
        {
            var declared = numeric.NestedTypes.FirstOrDefault(type => type.Name == name);
            var predicate = declared is null ? null : (Requirement.Refined(declared) as RefinedType)?.Predicate;
            var required = RealRequirement.Of(predicate, RealKind.Decimal);
            return required is not null && (negated ? required.Not() : required)
                .Contains(new LiteralValue((decimal)exact, "Standard.Decimal"));
        }

        return Contract(numeric, name) is { } contract && values.IsSubsetOf(negated ? contract.Not() : contract);
    }

    private static Admitted? Contract(IMistType numeric, string name)
    {
        return numeric.NestedTypes.FirstOrDefault(type => type.Name == name) is { } declared
            ? Requirement.ValuesOf(declared) : null;
    }
}