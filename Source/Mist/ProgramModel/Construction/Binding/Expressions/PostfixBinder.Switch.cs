using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static partial class PostfixBinder
{
    private static Expression BindSwitch(
        SwitchExpressionSuffixPostfixSuffixNode node, Expression scrutinee, BindingContext ctx, BuilderContext context)
    {
        if(node.Arms is null)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.SwitchExpressionNeedsAnArm, node, null);
            return new ErrorExpression(node);
        }

        var armNodes = new List<SwitchArmNode>(1 + node.Arms.SwitchArms.Length) { node.Arms.SwitchArm };
        armNodes.AddRange(node.Arms.SwitchArms);

        var arms = new List<SwitchArm>(armNodes.Count);
        foreach(var armNode in armNodes)
        {
            if(BindArm(armNode, scrutinee.Type, ctx, context, PriorFailure.Of(scrutinee), ResultTransfers.IsOwned(scrutinee)) is not { } arm)
            {
                return new UnresolvedExpression(node);
            }

            arms.Add(arm);
        }

        var resultType = ctx.TargetType ?? CommonArmType(arms, node, context);

        if(resultType is null || arms.All(arm => PriorFailure.Of(arm.Result)))
        {
            return new ErrorExpression(node, [new SwitchExpression(node, scrutinee, arms, resultType ?? ErrorType.Instance)]);
        }

        var converted = arms.ConvertAll(arm => new SwitchArm(
            arm.Node, arm.Pattern, arm.Guard, ImplicitConversions.At(arm.Result, resultType, context.ProgramModel.TypeInterner)));

        return new SwitchExpression(node, scrutinee, converted, resultType);
    }

    /// <summary>
    /// A scope of its own, so the bindings the pattern introduces reach this arm's guard and result and
    /// nothing else. As for a <see cref="StatementBinder"/> branch, the scope exists before the pattern
    /// binds into it.
    /// </summary>
    private static SwitchArm? BindArm(
        SwitchArmNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context, bool scrutineeFailed, bool ownsPayload)
    {
        var enclosing = ctx.Scope;
        ctx.Scope = new BodyScope(enclosing);
        var arm = BindArmParts(node, scrutineeType, ctx, context, scrutineeFailed, ownsPayload);
        ctx.Scope = enclosing;

        return arm;
    }

    private static SwitchArm? BindArmParts(
        SwitchArmNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context, bool scrutineeFailed, bool ownsPayload)
    {
        if(PatternBinder.Bind(node.Pattern, scrutineeType, ctx, context, scrutineeFailed, ownsPayload) is not { } pattern)
        {
            return null;
        }

        Expression? guard = null;
        if(node.Guard is { } when)
        {
            using(ctx.PushTargetType(null))
            {
                guard = StatementBinder.AsCondition(OperatorBinder.BindVoidCoalescing(when.Condition, ctx, context), context);
            }
        }

        return new SwitchArm(node, pattern, guard, ExpressionBinder.BindExpression(node.Result, ctx, context));
    }

    private static IMistType? CommonArmType(
        IReadOnlyList<SwitchArm> arms, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        var values = arms.Select(arm => arm.Result).ToList();
        var common = CommonTypeInference.Infer(values, context.Resolver.LookupWellKnown, context.ProgramModel.TypeInterner);
        if(common is null)
        {
            var (first, other) = CommonTypeInference.Disagreement(values);
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.SwitchArmsNoCommonType, node, null, DiagnosticSnapshot.Type(first), DiagnosticSnapshot.Type(other));
        }

        return common;
    }
}