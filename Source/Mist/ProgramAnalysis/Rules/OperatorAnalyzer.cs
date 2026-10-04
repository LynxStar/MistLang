using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// An operator no operator admits, a member of <c>unknown</c>, and conditional access on a receiver
/// that cannot be absent. Order 9 of the analysis register, the half whose subject is an expression.
/// The binder already typed each of these as an error so that nothing downstream complains twice;
/// this is where the complaint itself is made, against the operands, whose types are sound.
/// </summary>
internal sealed class OperatorAnalyzer : IProgramAnalyzer
{
    public string Id => "operator-and-positional-checks";

    public void Analyze(AnalysisContext context)
    {
        var judged = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(var node in context.Nodes)
        {
            visit(node);
        }

        void visit(object node)
        {
            if(!judged.Add(node))
            {
                return;
            }

            foreach(var child in BodyWalker.ChildrenOf(node))
            {
                visit(child);
            }

            switch(node)
            {
                case BinaryExpression binary when binary.Type is ErrorType:
                    JudgeBinary(context, binary);
                    break;

                case UnaryExpression unary when unary.Type is ErrorType
                    && !context.HasFailure(unary.Operand)
                    && OperatorDecision.Unary(unary.OperatorKind, unary.Operand.Type) is { } refused:
                    context.Refuse(
                        unary, refused, [],
                        Spelling(unary.OperatorKind), TypeDisplay.Mist(unary.Operand.Type), TypeDisplay.Mist(unary.Operand.Type));
                    break;

                case MemberAccessExpression { IsConditional: true } access
                    when OperatorDecision.Conditional(access.Receiver.Type) is { } conditional:
                    context.Refuse(
                        access, conditional, [access.Receiver],
                        TypeDisplay.Mist(access.Receiver.Type));
                    break;

                case MemberAccessExpression { Receiver.Type: UnknownType, Member: null or NotFoundResult, Type: ErrorType } access:
                    context.Refuse(access, OperatorDecision.Member(access.Receiver.Type)!.Value, [access.Receiver], access.MemberName);
                    break;
            }
        }
    }

    private static void JudgeBinary(AnalysisContext context, BinaryExpression binary)
    {
        IMistType wellKnown(WellKnownType type)
        {
            return context.Model.TypesByFQN[type.FullyQualifiedName];
        }

        if(OperatorDecision.Binary(binary.OperatorKind, binary.Left.Type, binary.Right.Type, wellKnown) is not { } descriptor)
        {
            return;
        }

        context.Refuse(
            binary, descriptor, [binary.Left, binary.Right],
            Spelling(binary.OperatorKind), TypeDisplay.Mist(binary.Left.Type), TypeDisplay.Mist(binary.Right.Type));
    }

    private static string Spelling(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate => "+",
            BinaryOperatorKind.Subtract => "-",
            BinaryOperatorKind.Multiply => "*",
            BinaryOperatorKind.Divide => "/",
            BinaryOperatorKind.Modulo => "%",
            BinaryOperatorKind.Equal => "==",
            BinaryOperatorKind.NotEqual => "!=",
            BinaryOperatorKind.LessThan => "<",
            BinaryOperatorKind.GreaterThan => ">",
            BinaryOperatorKind.LessThanOrEqual => "<=",
            BinaryOperatorKind.GreaterThanOrEqual => ">=",
            BinaryOperatorKind.LogicalAnd => "&&",
            BinaryOperatorKind.LogicalOr => "||",
            _ => kind.ToString(),
        };
    }

    private static string Spelling(UnaryOperatorKind kind)
    {
        return kind switch
        {
            UnaryOperatorKind.Negate => "-",
            UnaryOperatorKind.LogicalNot => "!",
            _ => kind.ToString(),
        };
    }
}