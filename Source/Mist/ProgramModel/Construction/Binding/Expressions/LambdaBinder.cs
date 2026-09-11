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
    private readonly record struct DeclaredParameter(string Name, TypeReferenceNode? Type, SyntaxNode Node);

    internal static Expression Bind(LambdaExpressionNode node, BindingContext ctx, BuilderContext context)
    {
        var (modifiers, parameterList, expressionBody, blockBody) = Parts(node);

        var isAsync = modifiers.Any(modifier => modifier is AsyncLambdaModifierLambdaModifierNode);
        var isStatic = modifiers.Any(modifier => modifier is StaticLambdaModifierLambdaModifierNode);
        var target = DelegateShape.Of(AliasTransparency.Expand(
            ctx.TargetType ?? ErrorType.Instance, context.ProgramModel.TypeInterner));
        var declared = ParameterNodes(parameterList);

        if(target is not null && target.ParameterTypes.Count != declared.Count)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.LambdaArityMismatch, node, null,
                target.Name, target.ParameterTypes.Count, declared.Count);

            return new ErrorExpression(node);
        }

        var enclosing = ctx.Scope;
        var scope = new BodyScope(enclosing);
        ctx.Scope = scope;

        try
        {
            if(DeclareParameters(declared, target, scope, ctx, context) is not { } parameters)
            {
                return new ErrorExpression(node);
            }

            // An async lambda's body is an async context and produces the RESULT, exactly as an async
            // method's does; the delegate it fills still says the task. Both are restored after,
            // because the lambda sits inside a body whose own context is unaffected by it.
            var enclosingIsAsync = ctx.IsAsyncContext;
            ctx.IsAsyncContext = isAsync || enclosingIsAsync;

            var bodyTarget = isAsync && target?.ReturnType is { } asyncReturn
                ? TaskResult.Of(asyncReturn) ?? asyncReturn
                : target?.ReturnType
                ;

            try
            {
                return expressionBody is not null
                    ? BindExpressionBodied(
                        node, expressionBody, parameters, isStatic, isAsync, target, bodyTarget, ctx, context)
                    : BindBlockBodied(
                        node, blockBody!, parameters, isStatic, isAsync, target, bodyTarget, ctx, context)
                    ;
            }
            finally
            {
                ctx.IsAsyncContext = enclosingIsAsync;
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
        IFunctionType? target,
        IMistType? bodyTarget,
        BindingContext ctx,
        BuilderContext context)
    {
        Expression result;
        using(ctx.PushTargetType(bodyTarget))
        {
            result = ExpressionBinder.BindExpression(body, ctx, context);
        }

        var declared = target?.ReturnType
            ?? (isAsync ? context.Resolver.ConstructTask(result.Type) : result.Type);

        return new LambdaExpression(
            node, parameters, result, body: null, isStatic,
            FunctionTypeOf(parameters, declared, context))
        {
            IsAsync = isAsync,
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
        IFunctionType? target,
        IMistType? bodyTarget,
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

        var produced = target?.ReturnType is { } declared && !isAsync
            ? declared
            : InferredReturnType(body, node, context)
                ?? context.Resolver.LookupWellKnown(WellKnownTypes.Void)
            ;

        var returnType = target?.ReturnType
            ?? (isAsync ? context.Resolver.ConstructTask(produced) : produced);

        return new LambdaExpression(
            node, parameters, result: null, body, isStatic,
            FunctionTypeOf(parameters, returnType, context))
        {
            IsAsync = isAsync,
        };
    }

    private static IMistType FunctionTypeOf(
        List<LocalVariable> parameters, IMistType returnType, BuilderContext context)
    {
        return context.ProgramModel.TypeInterner.GetOrCreateFunctionType(
            parameters.ConvertAll(parameter => parameter.Type), returnType);
    }

    /// <summary>
    /// The type a braced body produces, read off its own <c>return</c> statements. Shared with the
    /// async BLOCK expression, which is the same question about the same shape -- a braced body with no
    /// declared result -- reached through different syntax.
    /// </summary>
    internal static IMistType? InferredReturnType(Block body, SyntaxNode node, BuilderContext context)
    {
        IMistType? candidate = null;

        foreach(var returned in Returns(body))
        {
            if(returned.Value is not { } value)
            {
                continue;
            }

            if(candidate is null)
            {
                candidate = value.Type;
                continue;
            }

            if(!ReferenceEquals(candidate, value.Type))
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.LambdaReturnsNoCommonType, node, null,
                    candidate.Name, value.Type.Name);

                return ErrorType.Instance;
            }
        }

        return candidate;
    }

    /// <summary>
    /// Every <c>return</c> reachable in the lambda's own body. A NESTED lambda is not descended into: its
    /// returns answer to it and not to this one, which is the only reason this walk cannot simply visit
    /// every statement.
    /// </summary>
    private static IEnumerable<ReturnStatement> Returns(Statement statement)
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
        }
    }

    private static List<LocalVariable>? DeclareParameters(
        List<DeclaredParameter> declared,
        IFunctionType? target,
        BodyScope scope,
        BindingContext ctx,
        BuilderContext context)
    {
        var parameters = new List<LocalVariable>(declared.Count);

        for(var i = 0; i < declared.Count; i++)
        {
            var type = declared[i].Type is { } typeRef
                ? context.Resolver.Resolve(typeRef, ctx.Scope)
                : target?.ParameterTypes[i]
                ;

            if(type is null)
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.LambdaParameterNeedsAType, declared[i].Node, null, declared[i].Name);

                return null;
            }

            var local = new LocalVariable(declared[i].Name, type);
            scope.DeclareLocal(local, context.Diagnostics);
            parameters.Add(local);
        }

        return parameters;
    }

    private static List<DeclaredParameter> ParameterNodes(LambdaParameterListNode? parameterList)
    {
        if(parameterList is SingleImplicitParamLambdaParameterListNode single)
        {
            return [new DeclaredParameter(single.Name.Lexeme, null, single)];
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
                    new DeclaredParameter(typed.Name.Lexeme, typed.Type, typed),
                UntypedLambdaParamLambdaParamNode untyped =>
                    new DeclaredParameter(untyped.Name.Lexeme, null, untyped),
                _ => new DeclaredParameter("", null, parameter),
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