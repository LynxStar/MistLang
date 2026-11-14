using System.Numerics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Facts;

internal static class Exactness
{
    public static bool Holds(Expression operation, Term term, Bag bag, Budget? budget = null)
    {
        if(RealRequirement.KindOf(operation.Type) is { } kind)
        {
            return Real(operation, kind, term, bag, budget);
        }

        if(IntegralTypes.Range(operation.Type) is not { Low: { } low, High: { } high } || !Supported(operation))
        {
            return false;
        }

        var values = term.Variables.ToHashSet();
        var below = new Alternative([new Fact(term.Add(new Term(1 - low)))]);
        var above = new Alternative([new Fact(term.Scale(-1).Add(new Term(high + 1)))]);
        return Chain.Prove(bag, [below], values, budget) is Proven && Chain.Prove(bag, [above], values, budget) is Proven;
    }

    // Division passes the dividend as its term because a quotient is not a whole-number term; the caller records
    // `divisor * quotient == dividend`. Power-of-two division requires a normal quotient to avoid rounding tiny values.
    private static bool Real(Expression operation, RealKind kind, Term term, Bag bag, Budget? budget)
    {
        if(operation is UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true } })
        {
            return true;
        }

        return kind != RealKind.Decimal && operation switch
        {
            BinaryExpression { OperatorKind: BinaryOperatorKind.Multiply, Operator: null or { Modifiers.IsIntrinsic: true } } product
                when(Constant(product.Left) ?? Constant(product.Right)) is { } factor && RealNumbers.IsPowerOfTwo(BigInteger.Abs(factor))
                => Within(term, kind, bag, budget),
            BinaryExpression { OperatorKind: BinaryOperatorKind.Divide, Operator: null or { Modifiers.IsIntrinsic: true } } quotient
                when Constant(quotient.Right) is { } divisor && RealNumbers.IsPowerOfTwo(BigInteger.Abs(divisor))
                => Within(term, kind, bag, budget) && Normal(term, BigInteger.Abs(divisor), kind, bag, budget),
            _ => false,
        };
    }

    private static BigInteger? Constant(Expression operand)
    {
        return Terms.Read(operand, _ => null) is { Variables.Length: 0 } constant ? constant.Constant : null;
    }

    private static bool Within(Term term, RealKind kind, Bag bag, Budget? budget)
    {
        var largest = new Term(RealNumbers.Largest(kind));
        var values = term.Variables.ToHashSet();
        return Chain.Prove(bag, [new Alternative([RealFacts.Compare(term, ComparisonKind.LessThan, largest.Scale(-1))]),
            new Alternative([RealFacts.Compare(term, ComparisonKind.GreaterThan, largest)])], values, budget) is Proven;
    }

    private static bool Normal(Term dividend, BigInteger divisor, RealKind kind, Bag bag, Budget? budget)
    {
        var smallest = RealNumbers.SmallestNormal(kind);
        var edge = new Rational(smallest.Numerator * divisor, smallest.Denominator);
        var zero = new Term(0);
        return Chain.Prove(bag, [
            new Alternative([RealFacts.Compare(dividend, ComparisonKind.GreaterThan, edge with { Numerator = -edge.Numerator }), RealFacts.Compare(dividend, ComparisonKind.LessThan, zero)]),
            new Alternative([RealFacts.Compare(dividend, ComparisonKind.GreaterThan, zero), RealFacts.Compare(dividend, ComparisonKind.LessThan, edge)]),
        ], dividend.Variables.ToHashSet(), budget) is Proven;
    }

    private static bool Supported(Expression operation)
    {
        return operation switch
        {
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true } } => true,
            BinaryExpression { OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract, Operator: null or { Modifiers.IsIntrinsic: true } } => true,
            BinaryExpression { OperatorKind: BinaryOperatorKind.Multiply, Operator: null or { Modifiers.IsIntrinsic: true } } binary
                => Terms.Read(binary.Left, _ => null) is { Variables.Length: 0 } || Terms.Read(binary.Right, _ => null) is { Variables.Length: 0 },
            _ => false,
        };
    }
}