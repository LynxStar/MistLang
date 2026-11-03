using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

internal static class DecimalArithmeticProof
{
    public static ArithmeticProof.Verdict Judge(BinaryOperatorKind op, RealSet left, RealSet right)
    {
        var zero = new LiteralValue(0m, "Standard.Decimal");
        if(left.Intervals.Length == 0 || right.Intervals.Length == 0)
        {
            return ArithmeticProof.Verdict.Fallible;
        }

        if(op is BinaryOperatorKind.Divide or BinaryOperatorKind.Modulo)
        {
            if(right.IsSubsetOf(RealSet.Exactly(RealKind.Decimal, zero)!))
            {
                return ArithmeticProof.Verdict.Invalid;
            }

            if(right.Contains(zero))
            {
                return ArithmeticProof.Verdict.Fallible;
            }

            if(op == BinaryOperatorKind.Modulo)
            {
                return ArithmeticProof.Verdict.Safe;
            }
        }

        foreach(var a in left.Intervals)
        {
            foreach(var b in right.Intervals)
            {
                foreach(var x in new[] { a.Low.Decimal, a.High.Decimal })
                {
                    foreach(var y in new[] { b.Low.Decimal, b.High.Decimal })
                    {
                        if(Calculate(op, x, y) is null)
                        {
                            return Exact(left) is not null && Exact(right) is not null
                                ? ArithmeticProof.Verdict.Invalid : ArithmeticProof.Verdict.Fallible;
                        }
                    }
                }
            }
        }

        return ArithmeticProof.Verdict.Safe;
    }

    public static decimal? Exact(RealSet values)
    {
        return values.Intervals is [{ Low.Decimal: var low, High.Decimal: var high }] && low == high ? low : null;
    }

    public static decimal? Calculate(BinaryOperatorKind op, decimal left, decimal right)
    {
        try
        {
            return op switch
            {
                BinaryOperatorKind.Add => left + right,
                BinaryOperatorKind.Subtract => left - right,
                BinaryOperatorKind.Multiply => left * right,
                BinaryOperatorKind.Divide => left / right,
                BinaryOperatorKind.Modulo => left % right,
                _ => null,
            };
        }
        catch(ArithmeticException)
        {
            return null;
        }
    }
}