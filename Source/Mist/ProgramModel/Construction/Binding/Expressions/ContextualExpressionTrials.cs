using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal sealed class ContextualExpressionTrials(BindingContext ctx, BuilderContext context) : IContextualExpressionTrials
{
    private readonly Dictionary<(UnboundContextualExpression, IMistType), bool> outcomes = [];

    public bool Fits(UnboundContextualExpression expression, IMistType target)
    {
        if(!outcomes.TryGetValue((expression, target), out var fits))
        {
            Expression bound = null!;
            var trial = context.Diagnostics.Withhold(() => bound = context.ProgramModel.Discarding(
                () => BindAgainst(expression, target, ctx, context)));
            fits = !trial.Occurrences.Any(diagnostic => diagnostic.DefaultSeverity == DiagnosticSeverity.Error)
                && !PriorFailure.Of(bound)
                && AcceptsConstruction(bound)
                && ConversionClassifier.IsImplicit(bound.Type, target, context.ProgramModel.TypeInterner);
            trial.Discard();
            outcomes[(expression, target)] = fits;
        }

        return fits;
    }

    private bool AcceptsConstruction(Expression expression)
    {
        if(expression is not NewExpression { Constructor: { } constructor } creation)
        {
            return expression is not ErrorExpression;
        }

        if(!ValuePack.AcceptsCount(constructor.ParametersList, creation.Arguments.Count))
        {
            return false;
        }

        for(var i = 0; i < creation.Arguments.Count; i++)
        {
            var slot = ValuePack.SlotOf(constructor.ParametersList, creation.Arguments.Count, i);
            var target = creation.ParameterTypes?[slot] ?? constructor.ParametersList[slot].Type;
            if(ValuePack.ElementOf(constructor.ParametersList[slot]) is { } element
                && creation.Arguments.Count != constructor.ParametersList.Count)
            {
                target = element;
            }

            var argument = creation.Arguments[i];
            if(!AcceptsConstruction(argument)
                || (!ConversionClassifier.IsImplicit(argument.Type, target, context.ProgramModel.TypeInterner)
                    && !ConstantConversion.Fits(argument, target)))
            {
                return false;
            }
        }

        return true;
    }

    internal static Expression BindAgainst(
        UnboundContextualExpression expression, IMistType target, BindingContext ctx, BuilderContext context, bool targetIsFailed = false)
    {
        using(ctx.PushTargetType(target, targetIsFailed))
        {
            return ExpressionBinder.BindExpression(expression.Syntax, ctx, context);
        }
    }
}