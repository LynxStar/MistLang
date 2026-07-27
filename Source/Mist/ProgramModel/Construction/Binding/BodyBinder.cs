using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

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
    public static void BindDelegate(Method method, MemberType enclosingType, BuilderContext context)
    {
        var bodyScope = new Scope(method.Scope);
        foreach(var parameter in method.ParametersList)
        {
            bodyScope.RegisterLocal(new LocalVariable(parameter.Name, parameter.Type), context.Diagnostics);
        }

        var isStaticContext = method.Modifiers.IsStatic;

        // A constructor chain's arguments are bound in the same scope as the body, since
        // they reference the constructor's parameters, and before it, matching the order
        // they run in.
        if(method is Constructor { ChainNode: { } chain } constructor)
        {
            BindChainArguments(constructor, chain, bodyScope, enclosingType, isStaticContext, context);
        }

        BindBody(method.Body!, bodyScope, method.ReturnType, enclosingType, isStaticContext, context);
    }

    /// <summary>
    /// Binds the argument expressions of a <c>: this(...)</c> or <c>: base(...)</c> chain.
    /// </summary>
    private static void BindChainArguments(Constructor constructor, ConstructorChainNode chain, Scope scope, MemberType enclosingType, bool isStaticContext, BuilderContext context)
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

        var bindingCtx = new BindingContext(scope, returnType: null)
        {
            EnclosingType = enclosingType,
            IsStaticContext = isStaticContext,
        };

        var arguments = ArgumentBinder.Bind(argumentList, bindingCtx, context);

        constructor.ChainArgumentsList.AddRange(arguments);
    }

    /// <summary>
    /// Binds a body block: resolves statements in the given scope. Used directly by
    /// property/indexer accessors (whose parameters are already in scope) and
    /// indirectly by <see cref="BindDelegate"/>.
    /// </summary>
    public static void BindBody(Block body, Scope scope, IMistType? returnType, MemberType? enclosingType, bool isStaticContext, BuilderContext context)
    {
        var bindingCtx = new BindingContext(scope, returnType)
        {
            EnclosingType = enclosingType,
            IsStaticContext = isStaticContext,
        };

        var statements = new List<Statement>(body.Node.Statements.Length);
        foreach(var node in body.Node.Statements)
        {
            statements.Add(StatementBinder.BindStatement(node, bindingCtx, context));
        }

        body.Statements = statements;
    }
}