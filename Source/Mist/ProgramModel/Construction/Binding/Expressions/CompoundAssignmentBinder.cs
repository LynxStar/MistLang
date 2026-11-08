using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// <c>target op= value</c> and the increments, bound as <c>target = target op value</c> would be: the operator
/// is selected over the target's read and the value exactly as <see cref="OperatorBinder"/> selects it for a
/// <see cref="BinaryExpression"/>, so proof-sensitive arithmetic, wrapping and declared operators all carry over.
/// Whether the result may be written back is the assignability rule's, as it is for the long form. The operator
/// sees a <c>??=</c>-narrowed read while the slot stays the declared type, as for <c>=</c>; and <c>++</c> and
/// <c>--</c> are defined on the integral and real families alone, so a string's <c>+</c>, which is concatenation,
/// gives an increment no result.
/// </summary>
internal static class CompoundAssignmentBinder
{
    public static Expression Bind(
        SyntaxNode node, VoidCoalescingNode targetNode, string lexeme, ExpressionNode valueNode, BindingContext ctx, BuilderContext context)
    {
        Expression target;
        using(ctx.PushWriteTarget(AssignmentTargets.AccessNodeOf(targetNode), StorageAccess.ReadWrite))
        using(ctx.PushTargetType(null))
        {
            target = OperatorBinder.BindVoidCoalescing(targetNode, ctx, context);
        }

        Expression value;
        using(ctx.PushTargetType(Unnarrowed(target).Type))
        {
            value = ExpressionBinder.BindExpression(valueNode, ctx, context);
        }

        return Make(node, target, OperatorBinder.MapOperator(lexeme.TrimEnd('=')), value, IncrementForm.None, ctx, context);
    }

    public static Expression BindIncrement(
        SyntaxNode node, Expression target, string lexeme, IncrementForm form, BindingContext ctx, BuilderContext context)
    {
        var one = LiteralBinder.BindIntegerLiteral(node, "1", context);
        var op = lexeme == "++" ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract;

        if(!Increments(target.Type))
        {
            return new CompoundAssignmentExpression(node, Unnarrowed(target), op, one, Unnarrowed(target).Type, null, form)
            {
                ResultType = ErrorType.Instance,
            };
        }

        return Make(node, target, op, one, form, ctx, context);
    }

    public static bool Increments(IMistType type)
    {
        var underlying = OperatorAdmissibility.Underlying(type);
        return underlying is ErrorType or TypeParameter || OperatorAdmissibility.IsNumeric(underlying);
    }

    private static Expression Make(
        SyntaxNode node, Expression read, BinaryOperatorKind op, Expression value, IncrementForm form, BindingContext ctx, BuilderContext context)
    {
        var target = Unnarrowed(read);
        var operation = OperatorBinder.MakeBinary(node, read, op, value, ctx, context, out var verdict);

        return operation switch
        {
            BinaryExpression binary => new CompoundAssignmentExpression(node, target, op, binary.Right, target.Type, binary.Operator, form)
            {
                ResultType = binary.Type,
                OperatorClosing = binary.OperatorClosing,
                ParameterTypes = binary.ParameterTypes,
                AmbiguousOperators = binary.AmbiguousOperators,
            },
            InvocationExpression { Arguments: [_, var divisor] } helper => new CompoundAssignmentExpression(node, target, op, divisor, target.Type, null, form)
            {
                ResultType = helper.Type,
                GuardsRemainder = verdict == ArithmeticProof.Verdict.SafeGuardedRemainder,
            },
            _ => new CompoundAssignmentExpression(node, target, op, value, target.Type, null, form)
            {
                ResultType = operation.Type,
            },
        };
    }

    private static Expression Unnarrowed(Expression target)
    {
        return target is LocalReferenceExpression { IsNarrowed: true } narrowed
            ? new LocalReferenceExpression(narrowed.Node, narrowed.Local)
            : target;
    }
}