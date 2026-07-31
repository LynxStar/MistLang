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
    /// Accessors reach their enclosing type through a <see cref="MethodScope"/>, the same kind a
    /// method uses, because the one thing they need beyond a body is its narrowing of a static
    /// member's request away from an instance receiver.
    /// </summary>
    public static void BindAccessors(
        PropertyAccessor? getter,
        PropertyAccessor? setter,
        IMistType valueType,
        IReadOnlyList<Parameter> parameters,
        bool isStatic,
        Scope typeScope,
        BuilderContext context)
    {
        BindAccessor(getter, valueType, parameters, isStatic, isSetter: false, typeScope, context);
        BindAccessor(setter, valueType, parameters, isStatic, isSetter: true, typeScope, context);
    }

    private static void BindAccessor(
        PropertyAccessor? accessor,
        IMistType valueType,
        IReadOnlyList<Parameter> parameters,
        bool isStatic,
        bool isSetter,
        Scope typeScope,
        BuilderContext context)
    {
        if(accessor is null || accessor.IsAuto)
        {
            return;
        }

        var bodyScope = new BodyScope(new MethodScope(typeScope) { IsStatic = isStatic });

        foreach(var parameter in parameters)
        {
            bodyScope.DeclareLocal(new LocalVariable(parameter.Name, parameter.Type), context.Diagnostics);
        }

        if(isSetter)
        {
            bodyScope.DeclareLocal(new LocalVariable(ValueParameterName, valueType), context.Diagnostics);
        }

        // A getter's `return` carries the value; a setter has nothing to return, so it gets no
        // return type rather than a wrong one.
        var returnType = isSetter ? null : valueType;

        if(accessor.Body is { } body)
        {
            BindBody(body, bodyScope, returnType, context);
            return;
        }

        accessor.ExpressionBody = BindAccessorExpression(
            accessor.ExpressionBody, returnType, bodyScope, context);
    }

    private const string ValueParameterName = "value";

    private static Expression? BindAccessorExpression(Expression? parked, IMistType? targetType, BodyScope scope, BuilderContext context)
    {
        if(parked?.Node is not ExpressionNode expressionNode)
        {
            return parked;
        }

        var bindingCtx = new BindingContext(scope, returnType: null);

        // A getter's expression IS the value, so the property's type targets it. A setter's is a
        // side effect whose type nothing constrains.
        if(targetType is null)
        {
            return ExpressionBinder.BindExpression(expressionNode, bindingCtx, context);
        }

        using(bindingCtx.PushTargetType(targetType))
        {
            return ExpressionBinder.BindExpression(expressionNode, bindingCtx, context);
        }
    }

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