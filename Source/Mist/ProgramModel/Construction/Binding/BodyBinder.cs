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
    /// <param name="owningProperty">
    /// The property these accessors belong to, or null for an indexer's. What <c>field</c> resolves
    /// against, so passing null is also what refuses it: an indexer has no single slot behind it.
    /// </param>
    public static void BindAccessors(
        PropertyAccessor? getter,
        PropertyAccessor? setter,
        IMistType valueType,
        IReadOnlyList<Parameter> parameters,
        bool isStatic,
        Property? owningProperty,
        Scope typeScope,
        BuilderContext context)
    {
        BindAccessor(getter, valueType, parameters, isStatic, isSetter: false, owningProperty, typeScope, context);
        BindAccessor(setter, valueType, parameters, isStatic, isSetter: true, owningProperty, typeScope, context);
    }

    private static void BindAccessor(
        PropertyAccessor? accessor,
        IMistType valueType,
        IReadOnlyList<Parameter> parameters,
        bool isStatic,
        bool isSetter,
        Property? owningProperty,
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
            BindBody(body, bodyScope, returnType, context, owningProperty);
            return;
        }

        accessor.ExpressionBody = BindAccessorExpression(
            accessor.ExpressionBody, returnType, bodyScope, owningProperty, context);
    }

    private const string ValueParameterName = "value";

    private static Expression? BindAccessorExpression(
        Expression? parked, IMistType? targetType, BodyScope scope, Property? owningProperty, BuilderContext context)
    {
        if(parked?.Node is not ExpressionNode expressionNode)
        {
            return parked;
        }

        var bindingCtx = new BindingContext(scope, returnType: null) { EnclosingProperty = owningProperty };

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

    public static void BindBody(
        Block body, BodyScope scope, IMistType? returnType, BuilderContext context, Property? enclosingProperty = null)
    {
        BindStatements(body, new BindingContext(scope, returnType) { EnclosingProperty = enclosingProperty }, context);
    }

    /// <summary>
    /// Binds a block nested inside a body, under a scope of its own but everything else inherited.
    /// <para>
    /// Takes the enclosing context rather than its parts, so that whatever
    /// <see cref="BindingContext"/> comes to carry is inherited by construction. Forwarding field by
    /// field is what let <c>field</c> resolve at the top of a setter and fail one <c>if</c> deeper.
    /// </para>
    /// </summary>
    /// <param name="returnType">
    /// What a <c>return</c> in this block returns, where that differs from the enclosing member's --
    /// a lambda body being the case, since its returns answer to the lambda and not to the method
    /// around it. Null inherits the enclosing one.
    /// </param>
    public static void BindNestedBlock(
        Block block,
        BodyScope scope,
        BindingContext enclosing,
        BuilderContext context,
        IMistType? returnType = null)
    {
        BindStatements(
            block,
            new BindingContext(scope, returnType ?? enclosing.ReturnType) { EnclosingProperty = enclosing.EnclosingProperty },
            context);
    }

    private static void BindStatements(Block block, BindingContext bindingCtx, BuilderContext context)
    {
        var statements = new List<Statement>(block.Node.Statements.Length);
        foreach(var node in block.Node.Statements)
        {
            statements.Add(StatementBinder.BindStatement(node, bindingCtx, context));
        }

        block.Statements = statements;
    }
}