using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Entry point for Phase 3 body binding. Sets up the scope (parameter aliases)
/// and delegates to <see cref="StatementBinder"/> for the actual statement resolution.
/// </summary>
internal static class BodyBinder
{
    /// <summary>
    /// Binds a delegate (method, constructor, operator, cast operator): seeds the
    /// scope with parameter aliases, then binds the body.
    /// </summary>
    public static void BindDelegate(Method method, BuilderContext context)
    {
        var bodyScope = new BodyScope(method.Scope);
        foreach(var parameter in method.ParametersList)
        {
            bodyScope.DeclareLocal(new LocalVariable(parameter.Name, parameter.Type), context.Diagnostics);
        }

        // A constructor chain's arguments are bound in the same scope as the body, since
        // they reference the constructor's parameters, and before it, matching the order
        // they run in.
        if(method is Constructor { ChainNode: { } chain } constructor)
        {
            BindChainArguments(constructor, chain, bodyScope, context);
        }

        BindBody(method.Body!, bodyScope, method.ReturnType, context);
    }

    /// <summary>
    /// Binds the argument expressions of a <c>: this(...)</c> or <c>: base(...)</c> chain.
    /// </summary>
    private static void BindChainArguments(Constructor constructor, ConstructorChainNode chain, BodyScope scope, BuilderContext context)
    {
        var argumentList = chain switch
        {
            ThisChainConstructorChainNode thisChain => thisChain.Arguments,
            BaseChainConstructorChainNode baseChain => baseChain.Arguments,
            _ => null,
        };

        if(argumentList is null)
        {
            return;
        }

        var bindingCtx = new BindingContext(scope, returnType: null);
        var arguments = ArgumentBinder.Bind(argumentList, bindingCtx, context);

        constructor.ChainArgumentsList.AddRange(arguments);
    }

    /// <summary>
    /// Replaces the sentinel that Phase 2 parked with the resolved expression, returning null when
    /// there is no initializer so the caller can assign the result back unconditionally.
    /// <para>
    /// <see cref="InitializerScope"/> is where the no-receiver rule lives. The member's own type is
    /// pushed as target type because a declared type is the only target an initializer ever has.
    /// </para>
    /// </summary>
    public static Expression? BindInitializer(
        Expression? parked, IMistType memberType, Scope typeScope, BuilderContext context)
    {
        // Matched rather than cast so that a future caller parking something other than an
        // ExpressionNode leaves the initializer unbound instead of throwing.
        if(parked?.Node is not ExpressionNode initializerNode)
        {
            return parked;
        }

        var scope = new BodyScope(new InitializerScope(typeScope));
        var bindingCtx = new BindingContext(scope, returnType: null);

        using(bindingCtx.PushTargetType(memberType))
        {
            return ExpressionBinder.BindExpression(initializerNode, bindingCtx, context);
        }
    }

    /// <summary>
    /// Binds a body block: resolves statements in the given scope. Used directly by
    /// property/indexer accessors (whose parameters are already in scope) and
    /// indirectly by <see cref="BindDelegate"/>.
    /// </summary>
    public static void BindBody(Block body, BodyScope scope, IMistType? returnType, BuilderContext context)
    {
        var bindingCtx = new BindingContext(scope, returnType);
        var statements = new List<Statement>(body.Node.Statements.Length);
        foreach(var node in body.Node.Statements)
        {
            statements.Add(StatementBinder.BindStatement(node, bindingCtx, context));
        }

        body.Statements = statements;
    }
}