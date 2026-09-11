using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A function declared as a statement, in two passes. <see cref="Declare"/> runs over a whole
/// statement list before any statement of it is bound, so the name is in scope for every statement
/// of the list -- a call may precede the declaration, and two functions may call each other.
/// <see cref="BindBody"/> runs when the statement is reached, in a scope of its own, so the body sees
/// exactly the locals declared before it. The first pass stops at a <c>with</c> declaration, which
/// scopes the rest of the list into a body of its own that declares its functions when it is bound;
/// declaring them here too would make them callable from before the <c>with</c>, through a Method
/// whose body is never bound.
/// </summary>
internal static class LocalFunctionBinder
{
    public static void Declare(IReadOnlyList<StatementNode> nodes, BindingContext ctx, BuilderContext context)
    {
        foreach(var statement in nodes)
        {
            if(StatementBinder.ScopesTheTail(statement))
            {
                return;
            }

            if(statement is LocalFunctionDeclarationStatementStatementNode { LocalFunction: var node })
            {
                ctx.Scope.DeclareLocalFunction(node, Signature(node, ctx, context), context.Diagnostics);
            }
        }
    }

    private static Method Signature(LocalFunctionDeclarationNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;
        var modifiers = ModifierBuilder.Build(node, context.Diagnostics, declaringType: null);

        foreach(var refused in Refused(modifiers))
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.LocalFunctionModifier, node, null, refused);
        }

        if(node.TypeParams is not null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.LocalFunctionGeneric, node, null, name);
        }

        var parameters = new ParameterBuilder(context.Resolver, context.Attributes, context.Diagnostics)
            .Resolve(node.Parameters, ctx.Scope);
        var declaredReturn = context.Resolver.Resolve(node.ReturnType, ctx.Scope);
        var returnType = modifiers.IsAsync ? context.Resolver.ConstructTask(declaredReturn) : declaredReturn;

        var function = new Method(name, returnType, parameters, modifiers)
        {
            DeclarationNode = node,
            Body = new Block(node.Body),
            Scope = ctx.Scope,
        };

        if(node.TypeParams is null)
        {
            new ConstraintResolver(context, context.Resolver)
                .Resolve(node.Constraints, function.TypeParametersList, function.ParametersList, ctx.Scope);
        }

        return function;
    }

    private static IEnumerable<string> Refused(MemberModifiers modifiers)
    {
        if(modifiers.IsSealed)
        {
            yield return "sealed";
        }

        if(modifiers.IsIntrinsic)
        {
            yield return "intrinsic";
        }

        if(modifiers.IsReadonly)
        {
            yield return "readonly";
        }

        if(modifiers.IsOwned)
        {
            yield return "owned";
        }

        if(modifiers.IsRef)
        {
            yield return "ref";
        }

        if(modifiers.Discipline != SubstructuralDiscipline.None)
        {
            yield return modifiers.Discipline.ToString().ToLowerInvariant();
        }
    }

    public static Statement BindBody(LocalFunctionDeclarationNode node, BindingContext ctx, BuilderContext context)
    {
        var function = ctx.Scope.LocalFunctions[node];
        var scope = new BodyScope(ctx.Scope);
        var parameters = function.ParametersList.ConvertAll(BodyBinder.ParameterLocal);

        foreach(var parameter in parameters)
        {
            scope.DeclareLocal(parameter, context.Diagnostics);
        }

        var isAsync = function.Modifiers.IsAsync;
        var returnType = isAsync
            ? TaskResult.Of(function.ReturnType) ?? function.ReturnType
            : function.ReturnType
            ;

        var bodyCtx = new BindingContext(scope, returnType, ctx)
        {
            IsAsyncContext = isAsync,
            EnclosingMethod = function,
        };

        BodyBinder.BindNestedBlock(function.Body!, scope, bodyCtx, context, returnType);

        return new LocalFunctionStatement(function, parameters);
    }
}