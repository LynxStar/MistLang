using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
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
    public static void BindDelegate(Method method, BuilderContext context)
    {
        var bodyScope = new BodyScope(method.Scope);
        foreach(var parameter in method.ParametersList)
        {
            bodyScope.DeclareLocal(ParameterLocal(parameter), context.Diagnostics);
        }

        // A `with Type name` clause binds like a parameter and differs only in where the value comes
        // from: the caller passes a parameter, and the ambient context supplies this. Declared after
        // the parameters, so a clause reusing a parameter's name is the collision the scope reports
        // rather than a silent shadow.
        foreach(var binding in method.ContextBindingsList)
        {
            bodyScope.DeclareLocal(
                new LocalVariable(binding.Identifier, binding.ContextType), context.Diagnostics);
        }

        // A constructor chain's arguments are bound in the same scope as the body, since
        // they reference the constructor's parameters, and before it, matching the order
        // they run in.
        if(method is Constructor { ChainNode: { } chain } constructor)
        {
            BindChainArguments(constructor, chain, bodyScope, context);
        }

        // An async method's body returns the RESULT, not the task its signature advertises, so the
        // transform MethodBuilder applied is undone for the body's own target type.
        var isAsync = method.Modifiers.IsAsync;
        var returnType = isAsync
            ? TaskResult.Of(method.ReturnType) ?? method.ReturnType
            : method.ReturnType
            ;

        BindStatements(
            method.Body!,
            new BindingContext(bodyScope, returnType) { IsAsyncContext = isAsync, EnclosingMethod = method },
            context);
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

        var target = chain is BaseChainConstructorChainNode
            ? (constructor.DeclaringType as ConcreteType)?.BaseType
            : constructor.DeclaringType;

        if(target is null)
        {
            constructor.ChainArgumentsList.AddRange(ArgumentBinder.Bind(argumentList, bindingCtx, context));
            return;
        }

        var (arguments, choice) = ExpressionBinder.BindConstructorCall(target, argumentList, chain, bindingCtx, context);

        var interner = context.ProgramModel.TypeInterner;
        var parameterTypes = choice.Selected?.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner));

        constructor.ChainArgumentsList.AddRange(OwnedProvenance.AtParameters(
            ImplicitConversions.AtParameters(arguments, choice.Selected, parameterTypes, interner), choice.Selected?.ParametersList));
        constructor.ChainedConstructor = choice.Selected;
        constructor.ChainParameterTypes = parameterTypes;
    }

    /// <summary>
    /// Replaces the sentinel that Phase 2 parked with the resolved expression, returning null when
    /// there is no initializer so the caller can assign the result back unconditionally.
    /// <para>
    /// <see cref="InitializerScope"/> is where the no-receiver rule lives. The member's own type is
    /// pushed as target type because a declared type is the only target an initializer ever has; a
    /// member whose type the initializer itself decides passes none.
    /// </para>
    /// </summary>
    public static Expression? BindInitializer(
        Expression? parked, IMistType? memberType, Scope typeScope, BuilderContext context)
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
            return NewtypeLiterals.Admit(
                ExpressionBinder.BindExpression(initializerNode, bindingCtx, context), memberType, context.ProgramModel.TypeInterner);
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
            bodyScope.DeclareLocal(ParameterLocal(parameter), context.Diagnostics);
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
            return NewtypeLiterals.Admit(
                ExpressionBinder.BindExpression(expressionNode, bindingCtx, context), targetType, context.ProgramModel.TypeInterner);
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
            new BindingContext(scope, returnType, enclosing),
            context);
    }

    /// <summary>
    /// A synthetic block over statements that were written in an ENCLOSING one, for the tail a
    /// <c>with</c> declaration scopes. It borrows the enclosing block's AST node, having no node of
    /// its own -- the braces it stands for were never written.
    /// </summary>
    internal static Block BindStatementList(
        BlockNode enclosingNode,
        IReadOnlyList<StatementNode> nodes,
        BodyScope scope,
        BindingContext enclosing,
        BuilderContext context)
    {
        return new Block(enclosingNode)
        {
            Statements = BindList(
                enclosingNode, nodes, new BindingContext(scope, enclosing.ReturnType, enclosing), context),
        };
    }

    private static void BindStatements(Block block, BindingContext bindingCtx, BuilderContext context)
    {
        block.Statements = BindList(block.Node, block.Node.Statements, bindingCtx, context);
    }

    /// <summary>
    /// One statement list, however it was reached. A <c>with</c> DECLARATION scopes its name over the
    /// rest of the list, so the tail becomes its body and the walk stops -- consumed here rather than
    /// in StatementBinder because no other statement's meaning depends on what follows it, and only
    /// the list knows what does.
    /// <para>
    /// SHARED WITH THE TAIL IT PRODUCES, which is the whole reason it is one method. The tail is
    /// itself a statement list and may declare a `with` of its own; binding it through a plainer loop
    /// left every declaration after the first unrecognised, so its name resolved nowhere and only the
    /// FIRST of a run of them worked.
    /// </para>
    /// </summary>
    private static List<Statement> BindList(
        BlockNode enclosingNode,
        IReadOnlyList<StatementNode> nodes,
        BindingContext bindingCtx,
        BuilderContext context)
    {
        var statements = new List<Statement>(nodes.Count);
        LocalFunctionBinder.Declare(nodes, bindingCtx, context);

        for(var i = 0; i < nodes.Count; i++)
        {
            if(StatementBinder.BindWithDeclaration(
                enclosingNode, nodes[i], [.. nodes.Skip(i + 1)], bindingCtx, context) is { } scoped)
            {
                statements.Add(scoped);
                break;
            }

            statements.Add(StatementBinder.BindStatement(nodes[i], bindingCtx, context));
        }

        return statements;
    }

    internal static LocalVariable ParameterLocal(Parameter parameter)
    {
        return new LocalVariable(parameter.Name, parameter.Type)
        {
            Ownership = parameter.Modifier.Ownership,
            IsRef = parameter.Modifier.IsRef,
            Parameter = parameter,
        };
    }
}