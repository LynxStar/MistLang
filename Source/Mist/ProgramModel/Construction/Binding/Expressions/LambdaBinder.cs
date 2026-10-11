using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds lambda expressions. The target type says what the parameters are and what the result should be,
/// and inference supplies whatever it does not -- so a lambda in a typed slot need not restate the slot,
/// and one in an untyped slot must state enough to stand on its own.
/// </summary>
internal static class LambdaBinder
{
    private readonly record struct DeclaredParameter(string Name, TypeReferenceNode? Type, SyntaxNode Node, SourceSpan? NameSpan);

    internal static Expression Bind(LambdaExpressionNode node, BindingContext ctx, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;
        var target = ContextualLambdaTarget.Of(ctx.TargetType, interner);

        if(target is not null && target.ParameterTypes.Count != DeclaredParameterCount(node))
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.LambdaArityMismatch, node, null,
                target.Name, target.ParameterTypes.Count, DeclaredParameterCount(node));

            return new ErrorExpression(node);
        }

        var unionArm = ContextualLambdaTarget.GeneralUnionArmOf(ctx.TargetType, interner);
        var isGeneralUnion = ContextualLambdaTarget.IsGeneralUnion(ctx.TargetType, interner);
        return Bind(node, target?.ParameterTypes, target?.ReturnType, target?.IsPure ?? false, unionArm, isGeneralUnion, ctx, context);
    }

    internal static Expression BindWithParameters(
        LambdaExpressionNode node, IReadOnlyList<IMistType> parameterTypes, BindingContext ctx, BuilderContext context)
    {
        return parameterTypes.Count == DeclaredParameterCount(node)
            ? Bind(node, parameterTypes, returnType: null, targetIsPure: false, unionArm: null, isGeneralUnion: false, ctx, context)
            : new ErrorExpression(node);
    }

    internal static int DeclaredParameterCount(LambdaExpressionNode node)
    {
        return ParameterNodes(Parts(node).Params).Count;
    }

    internal static List<IMistType?> StatedParameterTypes(
        LambdaExpressionNode node, BindingContext ctx, BuilderContext context)
    {
        return ParameterNodes(Parts(node).Params)
            .ConvertAll(parameter => parameter.Type is { } typeRef ? context.Resolver.Resolve(typeRef, ctx.Scope) : null);
    }

    private static Expression Bind(
        LambdaExpressionNode node,
        IReadOnlyList<IMistType>? parameterTypes,
        IMistType? returnType,
        bool targetIsPure,
        IMistType? unionArm,
        bool isGeneralUnion,
        BindingContext ctx,
        BuilderContext context)
    {
        var (modifiers, parameterList, expressionBody, blockBody) = Parts(node);

        var isAsync = modifiers.Any(modifier => modifier is AsyncLambdaModifierLambdaModifierNode);
        var isStatic = modifiers.Any(modifier => modifier is StaticLambdaModifierLambdaModifierNode);
        var promises = new LambdaPromises(
            modifiers.Any(modifier => modifier is PureLambdaModifierLambdaModifierNode),
            modifiers.Any(modifier => modifier is LocalLambdaModifierLambdaModifierNode),
            targetIsPure);
        var declared = ParameterNodes(parameterList);

        if(promises.IsLocal && (promises.IsPure || promises.TargetIsPure))
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.PureAndLocal, node, null);
        }

        var enclosing = ctx.Scope;
        var scope = new BodyScope(enclosing);
        ctx.Scope = scope;

        try
        {
            if(DeclareParameters(declared, parameterTypes, scope, ctx, context) is not { } parameters)
            {
                return new ErrorExpression(node);
            }

            // An async lambda's body is an async context and produces the RESULT, exactly as an async
            // method's does; the delegate it fills still says the task. Both are restored after,
            // because the lambda sits inside a body whose own context is unaffected by it.
            var enclosingIsAsync = ctx.IsAsyncContext;
            var enclosingInLambda = ctx.InLambda;
            ctx.IsAsyncContext = isAsync || enclosingIsAsync;
            ctx.InLambda = true;

            var bodyTarget = isAsync && returnType is { } asyncReturn
                ? TaskResult.Of(asyncReturn) ?? asyncReturn
                : returnType
                ;

            try
            {
                return expressionBody is not null
                    ? BindExpressionBodied(
                        node, expressionBody, parameters, isStatic, isAsync, promises, returnType, bodyTarget, unionArm, isGeneralUnion, ctx, context)
                    : BindBlockBodied(
                        node, blockBody!, parameters, isStatic, isAsync, promises, returnType, bodyTarget, unionArm, isGeneralUnion, ctx, context)
                    ;
            }
            finally
            {
                ctx.IsAsyncContext = enclosingIsAsync;
                ctx.InLambda = enclosingInLambda;
            }
        }
        finally
        {
            ctx.Scope = enclosing;
        }
    }

    private static Expression BindExpressionBodied(
        LambdaExpressionNode node,
        ExpressionNode body,
        List<LocalVariable> parameters,
        bool isStatic,
        bool isAsync,
        LambdaPromises promises,
        IMistType? returnType,
        IMistType? bodyTarget,
        IMistType? unionArm,
        bool isGeneralUnion,
        BindingContext ctx,
        BuilderContext context)
    {
        Expression result;
        using(ctx.PushTargetType(bodyTarget))
        {
            result = ImplicitConversions.At(ExpressionBinder.BindExpression(body, ctx, context), bodyTarget, context.ProgramModel.TypeInterner);
        }

        var declared = returnType
            ?? (isAsync ? context.Resolver.ConstructTask(result.Type) : result.Type);

        var functionType = FunctionTypeOf(parameters, declared, promises, context);
        return new LambdaExpression(
            node, parameters, result, body: null, isStatic, functionType)
        {
            IsAsync = isAsync,
            IsPure = promises.IsPure,
            IsLocal = promises.IsLocal,
            UnionCarrierDelegateType = isGeneralUnion ? unionArm ?? functionType : null,
        };
    }

    /// <summary>
    /// A block body's result is read off its <c>return</c> statements where no target supplies one, which
    /// is why the block is bound BEFORE the type is known. A body that returns nothing is void.
    /// </summary>
    private static Expression BindBlockBodied(
        LambdaExpressionNode node,
        BlockNode bodyNode,
        List<LocalVariable> parameters,
        bool isStatic,
        bool isAsync,
        LambdaPromises promises,
        IMistType? returnType,
        IMistType? bodyTarget,
        IMistType? unionArm,
        bool isGeneralUnion,
        BindingContext ctx,
        BuilderContext context)
    {
        var body = new Block(bodyNode);

        // A nested block inherits the enclosing ReturnType where it is handed none, which is right for
        // a block and wrong for a lambda: an untargeted lambda's returns answer to it alone.
        var enclosingReturn = ctx.ReturnType;
        ctx.ReturnType = bodyTarget;

        try
        {
            BodyBinder.BindNestedBlock(body, ctx.Scope, ctx, context, bodyTarget);
        }
        finally
        {
            ctx.ReturnType = enclosingReturn;
        }

        var produced = returnType is { } declared && !isAsync
            ? declared
            : InferredReturnType(body, node, context)
                ?? context.Resolver.LookupWellKnown(WellKnownTypes.Void)
            ;

        var lambdaReturn = returnType
            ?? (isAsync ? context.Resolver.ConstructTask(produced) : produced);

        var functionType = FunctionTypeOf(parameters, lambdaReturn, promises, context);
        return new LambdaExpression(
            node, parameters, result: null, body, isStatic, functionType)
        {
            IsAsync = isAsync,
            IsPure = promises.IsPure,
            IsLocal = promises.IsLocal,
            UnionCarrierDelegateType = isGeneralUnion ? unionArm ?? functionType : null,
        };
    }

    /// <summary>
    /// Pure when written so or when the target is: the type is the pure one either way, so a lambda in a
    /// pure slot needs no conversion into it, and one written <c>pure</c> in a plain slot converts out.
    /// </summary>
    private static IMistType FunctionTypeOf(
        List<LocalVariable> parameters, IMistType returnType, LambdaPromises promises, BuilderContext context)
    {
        return context.ProgramModel.TypeInterner.GetOrCreateFunctionType(
            parameters.ConvertAll(parameter => parameter.Type), returnType, promises.IsPure || promises.TargetIsPure);
    }

    private readonly record struct LambdaPromises(bool IsPure, bool IsLocal, bool TargetIsPure);

    /// <summary>
    /// The type a braced body produces, read off its own <c>return</c> statements. Shared with the
    /// async BLOCK expression, which is the same question about the same shape -- a braced body with no
    /// declared result -- reached through different syntax.
    /// </summary>
    internal static IMistType? InferredReturnType(Block body, SyntaxNode node, BuilderContext context)
    {
        var values = Returns(body).Select(returned => returned.Value).OfType<Expression>().ToList();
        if(values.Count == 0)
        {
            return null;
        }

        var common = CommonTypeInference.Infer(values, context.Resolver.LookupWellKnown, context.ProgramModel.TypeInterner);
        if(common is null)
        {
            var (first, other) = CommonTypeInference.Disagreement(values);
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.LambdaReturnsNoCommonType, node, null, first.Name, other.Name);
            return ErrorType.Instance;
        }

        if(common is not ErrorType)
        {
            _ = SettleReturns(body, common, context);
        }

        return common;
    }

    private static Statement SettleReturns(Statement statement, IMistType target, BuilderContext context)
    {
        switch(statement)
        {
            case ReturnStatement returned:
                var returnTarget = returned.TargetType ?? target;
                return new ReturnStatement(
                    returned.Value is { } value ? ImplicitConversions.At(value, returnTarget, context.ProgramModel.TypeInterner) : null,
                    returnTarget, returned.IsRef);

            case Block block:
                block.Statements = block.Statements.Select(nested => SettleReturns(nested, target, context)).ToList();
                break;

            case IfStatement branch:
                return new IfStatement(
                    branch.Condition, (Block)SettleReturns(branch.ThenBranch, target, context),
                    branch.ElseBranch is { } otherwise ? SettleReturns(otherwise, target, context) : null, branch.ConditionBinds);

            case WhileStatement loop:
                _ = SettleReturns(loop.Body, target, context);
                break;

            case ForStatement loop:
                _ = SettleReturns(loop.Body, target, context);
                break;

            case ForEachStatement loop:
                _ = SettleReturns(loop.Body, target, context);
                break;

            case WithBlockStatement scoped:
                _ = SettleReturns(scoped.Body, target, context);
                break;
        }

        return statement;
    }

    /// <summary>
    /// Every <c>return</c> reachable in the lambda's own body. A NESTED lambda is not descended into: its
    /// returns answer to it and not to this one, which is the only reason this walk cannot simply visit
    /// every statement.
    /// </summary>
    internal static IEnumerable<ReturnStatement> Returns(Statement statement)
    {
        switch(statement)
        {
            case ReturnStatement returned:
                yield return returned;
                break;

            case Block block:
                foreach(var nested in block.Statements)
                {
                    foreach(var inner in Returns(nested))
                    {
                        yield return inner;
                    }
                }

                break;

            case IfStatement branch:
                foreach(var inner in Returns(branch.ThenBranch))
                {
                    yield return inner;
                }

                if(branch.ElseBranch is { } elseBranch)
                {
                    foreach(var inner in Returns(elseBranch))
                    {
                        yield return inner;
                    }
                }

                break;

            case WhileStatement loop:
                foreach(var inner in Returns(loop.Body))
                {
                    yield return inner;
                }

                break;

            case ForStatement loop:
                foreach(var inner in Returns(loop.Body))
                {
                    yield return inner;
                }

                break;

            case ForEachStatement loop:
                foreach(var inner in Returns(loop.Body))
                {
                    yield return inner;
                }

                break;

            case WithBlockStatement scoped:
                foreach(var inner in Returns(scoped.Body))
                {
                    yield return inner;
                }

                break;
        }
    }

    private static List<LocalVariable>? DeclareParameters(
        List<DeclaredParameter> declared,
        IReadOnlyList<IMistType>? parameterTypes,
        BodyScope scope,
        BindingContext ctx,
        BuilderContext context)
    {
        var parameters = new List<LocalVariable>(declared.Count);

        for(var i = 0; i < declared.Count; i++)
        {
            TypeResolution? resolution = null;
            if(declared[i].Type is { } typeRef)
            {
                resolution = context.Resolver.ResolveWithFailure(typeRef, ctx.Scope);
            }
            else if(parameterTypes?[i] is { } inferred)
            {
                resolution = new TypeResolution(inferred);
            }

            var type = resolution?.Type;

            if(type is null)
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.LambdaParameterNeedsAType, declared[i].Node, null, declared[i].Name);

                return null;
            }

            var local = new LocalVariable(declared[i].Name, type) { IsFailed = resolution?.IsFailed == true };
            scope.DeclareLocal(local, declared[i].Node, context.Diagnostics, declared[i].NameSpan);
            parameters.Add(local);
        }

        return parameters;
    }

    private static List<DeclaredParameter> ParameterNodes(LambdaParameterListNode? parameterList)
    {
        if(parameterList is SingleImplicitParamLambdaParameterListNode single)
        {
            return [new DeclaredParameter(single.Name.Lexeme, null, single, single.Name.Span)];
        }

        if(parameterList is not ParenthesizedLambdaParamsLambdaParameterListNode { Params: { } list })
        {
            return [];
        }

        var declared = new List<DeclaredParameter>();

        foreach(var parameter in GrammarRepetition.LeadAndRepeated(list.LambdaParam, list.LambdaParams))
        {
            declared.Add(parameter switch
            {
                TypedLambdaParamLambdaParamNode typed =>
                    new DeclaredParameter(typed.Name.Lexeme, typed.Type, typed, typed.Name.Span),
                UntypedLambdaParamLambdaParamNode untyped =>
                    new DeclaredParameter(untyped.Name.Lexeme, null, untyped, untyped.Name.Span),
                _ => new DeclaredParameter("", null, parameter, null),
            });
        }

        return declared;
    }

    private static (IReadOnlyList<LambdaModifierNode> Modifiers,
        LambdaParameterListNode? Params,
        ExpressionNode? ExpressionBody,
        BlockNode? BlockBody) Parts(LambdaExpressionNode node)
    {
        return node switch
        {
            ExpressionLambdaLambdaExpressionNode expression =>
                (expression.Modifiers, expression.Params, expression.Body, null),
            BlockLambdaLambdaExpressionNode block =>
                (block.Modifiers, block.Params, null, block.Body),
            _ => ([], null, null, null),
        };
    }
}