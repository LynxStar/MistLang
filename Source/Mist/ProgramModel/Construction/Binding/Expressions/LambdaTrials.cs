using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Answers selection's questions about one call's unbound lambdas by binding each against what a
/// candidate would give it, withholding the diagnostics and discarding the registries, so a binding made
/// under a candidate that loses leaves no trace. Each answer is kept per lambda and types, since every
/// candidate asks what the lambda returns and, from two candidates up, whether it fits.
/// <para>
/// A trial sees what BINDING reports and what the body's result type says, not what Stage 5 will refuse
/// in the finished model: an operator no type admits types as an error without a word from the binder,
/// which is why an error-typed result does not fit, but a refusal buried deeper in the body is invisible
/// here and the candidate stays applicable.
/// </para>
/// </summary>
internal sealed class LambdaTrials(BindingContext ctx, BuilderContext context) : ILambdaTrials
{
    private readonly Dictionary<UnboundLambdaExpression, IReadOnlyList<IMistType?>> stated = [];

    private readonly Dictionary<(UnboundLambdaExpression, IMistType), Trial> byTarget = [];

    private readonly List<(UnboundLambdaExpression Lambda, IReadOnlyList<IMistType> Parameters, Trial Outcome)> byParameters = [];

    private sealed record Trial(Expression Bound, bool Failed);

    public IReadOnlyList<IMistType?> StatedParameterTypes(UnboundLambdaExpression lambda)
    {
        if(!stated.TryGetValue(lambda, out var types))
        {
            types = Run(() => LambdaBinder.StatedParameterTypes(lambda.Lambda, ctx, context)).Bound;
            stated[lambda] = types;
        }

        return types;
    }

    public IMistType? ResultType(UnboundLambdaExpression lambda, IReadOnlyList<IMistType> parameterTypes)
    {
        var existing = byParameters.Find(entry => ReferenceEquals(entry.Lambda, lambda)
            && entry.Parameters.Count == parameterTypes.Count
            && entry.Parameters.Zip(parameterTypes).All(pair => ReferenceEquals(pair.First, pair.Second)));

        var trial = existing.Outcome;

        if(trial is null)
        {
            trial = Bind(() => LambdaBinder.BindWithParameters(lambda.Lambda, parameterTypes, ctx, context));
            byParameters.Add((lambda, parameterTypes, trial));
        }

        return trial.Bound is LambdaExpression { Type: IFunctionType function } ? function.ReturnType : ErrorType.Instance;
    }

    public bool Fits(UnboundLambdaExpression lambda, IMistType parameterType)
    {
        var interner = context.ProgramModel.TypeInterner;

        if(!byTarget.TryGetValue((lambda, parameterType), out var trial))
        {
            trial = Bind(() => BindAgainst(lambda, parameterType, ctx, context));
            byTarget[(lambda, parameterType)] = trial;
        }

        if(trial.Failed || trial.Bound is not LambdaExpression bound)
        {
            return false;
        }

        return DelegateShape.Of(AliasTransparency.Expand(parameterType, interner)) is { } shape
            ? Same(bound.Parameters.Select(parameter => parameter.Type).ToList(), shape.ParameterTypes)
                && Produces(bound, shape.ReturnType, interner)
            : ConversionRelation.IsImplicit(bound.Type, parameterType);
    }

    public IMistType ComparedType(UnboundLambdaExpression lambda, IMistType left, IMistType right)
    {
        var interner = context.ProgramModel.TypeInterner;

        if(DelegateShape.Of(AliasTransparency.Expand(left, interner)) is { } leftShape
            && DelegateShape.Of(AliasTransparency.Expand(right, interner)) is { } rightShape
            && Same(leftShape.ParameterTypes, rightShape.ParameterTypes)
            && ResultType(lambda, leftShape.ParameterTypes) is { } result and not ErrorType)
        {
            return interner.GetOrCreateFunctionType(leftShape.ParameterTypes, result);
        }

        return UnboundLambdaType.Instance;
    }

    private static bool Same(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count
            && left.Zip(right).All(pair => ReferenceEquals(pair.First, pair.Second) || ConversionRelation.IsIdentity(pair.First, pair.Second));
    }

    internal static Expression BindAgainst(
        UnboundLambdaExpression lambda, IMistType target, BindingContext ctx, BuilderContext context)
    {
        using(ctx.PushTargetType(target))
        {
            return LambdaBinder.Bind(lambda.Lambda, ctx, context);
        }
    }

    private Trial Bind(Func<Expression> bind)
    {
        var (bound, failed) = Run(() =>
        {
            using(ctx.PushTargetType(null))
            {
                return bind();
            }
        });

        return new Trial(bound, failed || bound is ErrorExpression);
    }

    private (T Bound, bool Failed) Run<T>(Func<T> trial)
    {
        T bound = default!;
        var withheld = context.Diagnostics.Withhold(() => bound = context.ProgramModel.Discarding(trial));

        return (bound, withheld.Exists(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static bool Produces(LambdaExpression lambda, IMistType returnType, TypeInterner interner)
    {
        var expected = lambda.IsAsync ? TaskResult.Of(returnType) ?? returnType : returnType;

        if(OptionalUnion.IsVoid(expected))
        {
            return true;
        }

        var results = lambda.Result is { } result
            ? Enumerable.Repeat(result, 1)
            : LambdaBinder.Returns(lambda.Body!).Select(returned => returned.Value).OfType<Expression>();

        return results.All(value => value is ImplicitConversionExpression
            || (value.Type is not ErrorType && ConversionClassifier.IsImplicit(value.Type, expected, interner))
            || ConstantConversion.Fits(value, expected));
    }
}