using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Facts;

internal static class Exactness
{
    public static bool Holds(Expression operation, Term term, Bag bag, Budget? budget = null)
    {
        if(IntegralTypes.Range(operation.Type) is not { Low: { } low, High: { } high } || !Supported(operation))
        {
            return false;
        }

        var values = term.Variables.ToHashSet();
        var below = new Alternative([new Fact(term.Add(new Term(1 - low)))]);
        var above = new Alternative([new Fact(term.Scale(-1).Add(new Term(high + 1)))]);
        return Chain.Prove(bag, [below], values, budget) is Proven && Chain.Prove(bag, [above], values, budget) is Proven;
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