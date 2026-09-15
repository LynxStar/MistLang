using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using StormSyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Resolves AST statement nodes into the model's <see cref="Statement"/> hierarchy.
/// All methods are static; shared dependencies are accessed via <see cref="BuilderContext"/>.
/// </summary>
internal static class StatementBinder
{
    public static Statement BindStatement(StatementNode statement, BindingContext ctx, BuilderContext context)
    {
        return statement switch
        {
            VariableDeclarationStatementStatementNode declaration => BindVariableDeclaration(declaration, ctx, context),
            ReturnStatementStatementNode returnStatement => BindReturn(returnStatement.ReturnStatement, ctx, context),
            IfStatementStatementNode ifStatement => BindIf(ifStatement.IfStatement, ctx, context),
            WhileStatementStatementNode whileStatement => BindWhile(whileStatement.WhileStatement, ctx, context),
            ForStatementStatementNode forStatement => BindFor(forStatement.ForStatement, ctx, context),
            ForeachStatementStatementNode foreachStatement => BindForEach(foreachStatement.ForeachStatement, ctx, context),
            BreakStatementStatementNode => new BreakStatement(),
            ExpressionStatementStatementNode exprStatement => BindExpressionStatement(exprStatement, ctx, context),
            BlockStatementStatementNode nestedBlock => BindNestedBlock(nestedBlock.BlockStatement, ctx, context),
            UninitializedVariableStatementStatementNode uninitialized => BindUninitialized(
                uninitialized.UninitializedVariable, ctx, context),
            WithVarBlockStatementStatementNode withVarBlock => BindWithBinding(
                withVarBlock.WithVarBlockStatement.Name.Lexeme,
                withVarBlock.WithVarBlockStatement.Initializer,
                withVarBlock.WithVarBlockStatement.Body,
                withVarBlock.WithVarBlockStatement.Body.Statements,
                ctx,
                context),
            WithBlockStatementStatementNode withBlock => BindWithBlock(
                withBlock.WithBlockStatement, ctx, context),
            ReadonlyBlockStatementStatementNode readonlyBlock => BindNestedBlock(
                readonlyBlock.ReadonlyBlock.Body, ctx, context, isReadonly: true),
            LocalFunctionDeclarationStatementStatementNode localFunction => LocalFunctionBinder.BindBody(
                localFunction.LocalFunction, ctx, context),
            _ => new UnsupportedStatement(statement),
        };
    }

    /// <summary>
    /// <c>T x;</c>, a declaration with no initializer. The local is DECLARED anyway -- that is what
    /// makes every later assignment and read resolve -- and whether it holds a value before it is
    /// read is a judgement on the finished body, not a reason to leave the name unbound.
    /// </summary>
    private static Statement BindUninitialized(
        UninitializedVariableDeclarationNode node, BindingContext ctx, BuilderContext context)
    {
        var local = new LocalVariable(node.Name.Lexeme, context.Resolver.Resolve(node.Type, ctx.Scope))
        {
            IsRef = MemberTypeBuilder.IsRefSpecifier(node.OwnershipSpecifier),
            IsReadonly = IsReadonlyBinding(hasReadonlyModifier: false, node.OwnershipSpecifier),
        };

        ctx.Scope.DeclareLocal(local, context.Diagnostics);
        return new LocalDeclarationStatement(local, initializer: null);
    }

    internal static bool ScopesTheTail(StatementNode node)
    {
        return node is WithVarDeclarationStatementStatementNode
            or WithTypedDeclarationStatementStatementNode
            or WithDiscardDeclarationStatementStatementNode
            or WithPushStatementStatementNode;
    }

    /// <summary>
    /// The DECLARATION forms of <c>with</c>, whose body is whatever follows them in the enclosing
    /// block. Null when this statement is not one, which is how the block binder tells them apart
    /// without knowing their shapes.
    /// </summary>
    internal static Statement? BindWithDeclaration(
        BlockNode enclosingNode,
        StatementNode node,
        IReadOnlyList<StatementNode> rest,
        BindingContext ctx,
        BuilderContext context)
    {
        return node switch
        {
            WithVarDeclarationStatementStatementNode declaration => BindWithBinding(
                declaration.WithVarDeclaration.Name.Lexeme,
                declaration.WithVarDeclaration.Initializer,
                enclosingNode,
                rest,
                ctx,
                context),
            WithTypedDeclarationStatementStatementNode typed => BindWithBinding(
                typed.WithTypedDeclaration.Name.Lexeme,
                typed.WithTypedDeclaration.Initializer,
                enclosingNode,
                rest,
                ctx,
                context,
                context.Resolver.Resolve(typed.WithTypedDeclaration.Type, ctx.Scope)),

            // The discard and push forms scope nothing, so they name no local; they differ only in
            // whether the key was written or is the value's own.
            WithDiscardDeclarationStatementStatementNode discard => BindWithBinding(
                name: null,
                discard.WithDiscardDeclaration.Initializer,
                enclosingNode,
                rest,
                ctx,
                context,
                context.Resolver.Resolve(discard.WithDiscardDeclaration.Type, ctx.Scope)),

            WithPushStatementStatementNode push => BindWithBinding(
                name: null,
                push.WithPushStatement.Value,
                enclosingNode,
                rest,
                ctx,
                context),

            _ => null,
        };
    }

    /// <summary>
    /// A <c>with</c> whose value is NAMED. The name is declared in the body's own scope, so it is
    /// visible exactly where the value is alive and nowhere after.
    /// </summary>
    /// <summary>
    /// The four declaration forms of <c>with</c>, which differ only in whether a name is bound and
    /// whether the key type is written. A null <paramref name="name"/> is the discard and push forms,
    /// which scope no name; a null <paramref name="keyType"/> takes the value's own.
    /// <para>
    /// The scope is created either way, because the tail is the statement's BODY whether or not
    /// anything new is visible in it.
    /// </para>
    /// </summary>
    private static Statement BindWithBinding(
        string? name,
        ExpressionNode initializer,
        BlockNode bodyNode,
        IReadOnlyList<StatementNode> body,
        BindingContext ctx,
        BuilderContext context,
        IMistType? keyType = null)
    {
        var value = ExpressionBinder.BindExpression(initializer, ctx, context);
        var scope = new BodyScope(ctx.Scope);
        LocalVariable? local = null;

        if(name is not null)
        {
            local = new LocalVariable(name, keyType ?? value.Type) { IsFailed = keyType is null && PriorFailure.Of(value) };
            scope.DeclareLocal(local, context.Diagnostics);
        }

        var block = BodyBinder.BindStatementList(bodyNode, body, scope, ctx, context);
        return new WithBlockStatement(value, block, local, keyType);
    }

    private static Statement BindWithBlock(
        WithBlockStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var value = ExpressionBinder.BindExpression(node.Expression, ctx, context);
        return new WithBlockStatement(value, BindNestedBlock(node.Body, ctx, context));
    }

    private static Block BindNestedBlock(
        BlockNode node, BindingContext ctx, BuilderContext context, bool isReadonly = false)
    {
        var block = new Block(node) { IsReadonly = isReadonly };
        BodyBinder.BindNestedBlock(block, new BodyScope(ctx.Scope), ctx, context);
        return block;
    }

    private static ExpressionStatement BindExpressionStatement(ExpressionStatementStatementNode statement, BindingContext ctx, BuilderContext context)
    {
        var expression = ExpressionBinder.BindExpression(statement.ExpressionStatement.Expression, ctx, context);

        if(expression is AssignmentExpression { Target: LocalReferenceExpression { Local: var local } } assignment && ctx.Scope is BodyScope body)
        {
            if(assignment.IsCoalescing && OptionalUnion.InnerType(local.Type) is { } inner && !OptionalUnion.ContainsVoid(assignment.Value.Type))
            {
                body.Narrow(local, inner);
            }
            else if(!assignment.IsCoalescing && (OptionalUnion.ContainsVoid(assignment.Value.Type) || OptionalUnion.IsVoid(assignment.Value.Type)))
            {
                body.Narrow(local, null);
            }
        }

        return new ExpressionStatement(expression);
    }

    /// <summary>
    /// A binding's <c>readonly</c>, from either shape it parses into. The grammar's greedy
    /// <c>READONLY?</c> sits ahead of the specifier, so <c>readonly ref</c> normally arrives as the
    /// token plus a bare ref specifier -- but the combined <c>ReadonlyRefSpecifier</c> alternative
    /// exists, and testing only one of the two would miss half the spellings.
    /// </summary>
    private static bool IsReadonlyBinding(bool hasReadonlyModifier, OwnershipSpecifierNode? specifier)
    {
        return hasReadonlyModifier || specifier is ReadonlyRefSpecifierOwnershipSpecifierNode;
    }

    private static Statement BindVariableDeclaration(VariableDeclarationStatementStatementNode statement, BindingContext ctx, BuilderContext context)
    {
        switch(statement.VariableDeclaration)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                {
                    var declaredType = context.Resolver.Resolve(typed.Type, ctx.Scope);

                    Expression? initializer = null;
                    if(typed.Initializer is not null)
                    {
                        using(ctx.PushTargetType(declaredType))
                        {
                            initializer = ExpressionBinder.BindExpression(typed.Initializer, ctx, context);
                        }

                        initializer = ImplicitConversions.At(
                            NewtypeLiterals.Admit(initializer, declaredType, context.ProgramModel.TypeInterner)!,
                            declaredType,
                            context.ProgramModel.TypeInterner);
                    }

                    var ownership = MemberTypeBuilder.ResolveOwnership(typed.OwnershipSpecifier);
                    var local = new LocalVariable(typed.Name.Lexeme, declaredType)
                    {
                        IsRef = MemberTypeBuilder.IsRefSpecifier(typed.OwnershipSpecifier),
                        IsReadonly = IsReadonlyBinding(typed.ReadonlyModifier is not null, typed.OwnershipSpecifier),
                        Ownership = ownership,
                    };
                    ctx.Scope.DeclareLocal(local, context.Diagnostics);
                    return new LocalDeclarationStatement(
                        local,
                        initializer is null ? null : OwnedProvenance.At(ownership, initializer));
                }

            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                {
                    var initializer = ExpressionBinder.BindExpression(inferred.Initializer, ctx, context);
                    var inferredType = initializer.Type;
                    var inferredOwnership = MemberTypeBuilder.ResolveOwnership(inferred.OwnershipSpecifier);
                    var local = new LocalVariable(inferred.Name.Lexeme, inferredType)
                    {
                        IsRef = MemberTypeBuilder.IsRefSpecifier(inferred.OwnershipSpecifier),
                        IsReadonly = IsReadonlyBinding(inferred.ReadonlyModifier is not null, inferred.OwnershipSpecifier),
                        Ownership = inferredOwnership,
                        IsFailed = PriorFailure.Of(initializer),
                    };
                    ctx.Scope.DeclareLocal(local, context.Diagnostics);
                    return new LocalDeclarationStatement(
                        local, OwnedProvenance.At(inferredOwnership, initializer));
                }

            case VarTupleDeconstructionVariableDeclarationStatementNode inferredTuple:
                return BindInferredDeconstruction(inferredTuple, ctx, context);

            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
                return BindTypedDeconstruction(typedTuple, ctx, context);

            default:
                return new UnsupportedStatement(statement);
        }
    }

    /// <summary>
    /// <c>var (a, b) = source;</c>. Each target takes the type of the element it reads, so a target over
    /// an <c>unknown</c> element is itself <c>unknown</c> and narrows from there.
    /// </summary>
    private static Statement BindInferredDeconstruction(
        VarTupleDeconstructionVariableDeclarationStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var initializer = ExpressionBinder.BindExpression(node.Initializer, ctx, context);
        var names = GrammarRepetition.LeadAndRepeated(node.FirstName, node.RestNames);

        if(ElementTypesFor(initializer, names.Count, node, context) is not { } elementTypes)
        {
            return new UnsupportedStatement(node);
        }

        var targets = new List<LocalVariable>(names.Count);

        for(var i = 0; i < names.Count; i++)
        {
            var local = new LocalVariable(names[i].Lexeme, elementTypes[i]);
            ctx.Scope.DeclareLocal(local, context.Diagnostics);
            targets.Add(local);
        }

        return new TupleDeconstructionStatement(targets, initializer);
    }

    /// <summary>
    /// <c>(int a, string b) = source;</c>. The declared target types are assembled into a tuple and
    /// pushed as the initializer's target type, so a tuple LITERAL on the right binds each of its
    /// elements against the target that will hold it.
    /// </summary>
    private static Statement BindTypedDeconstruction(
        TypedTupleDeconstructionVariableDeclarationStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var elementNodes = GrammarRepetition.TwoLeadsAndRepeated(
            node.First, node.Rest, node.TupleDeconstructionElements);

        var declaredTypes = elementNodes.ConvertAll(
            element => context.Resolver.Resolve(element.Type, ctx.Scope));

        Expression initializer;
        using(ctx.PushTargetType(context.ProgramModel.TypeInterner.GetOrCreateTupleType(declaredTypes)))
        {
            initializer = ExpressionBinder.BindExpression(node.Initializer, ctx, context);
        }

        var elementTypes = ElementTypesFor(initializer, elementNodes.Count, node, context);
        var interner = context.ProgramModel.TypeInterner;
        var targets = new List<LocalVariable>(elementNodes.Count);

        for(var i = 0; i < elementNodes.Count; i++)
        {
            var local = new LocalVariable(elementNodes[i].Name.Lexeme, declaredTypes[i]);
            ctx.Scope.DeclareLocal(local, context.Diagnostics);
            targets.Add(local);
        }

        var conversions = elementTypes?.Select((element, i) => ImplicitConversions.At(
            new ElementReadExpression(node.Initializer, element), declaredTypes[i], interner) as ImplicitConversionExpression).ToList();

        return new TupleDeconstructionStatement(targets, initializer, conversions);
    }

    /// <summary>
    /// The element types a deconstruction reads, or null where the source cannot supply
    /// <paramref name="targetCount"/> of them. An error-typed source answers null WITHOUT reporting,
    /// whatever produced it having reported already.
    /// </summary>
    private static IReadOnlyList<IMistType>? ElementTypesFor(
        Expression initializer, int targetCount, StormSyntaxNode node, BuilderContext context)
    {
        if(initializer.Type is Model.Types.ErrorType)
        {
            return null;
        }

        if(initializer.Type is not ITupleType tuple)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.DeconstructionSourceNotATuple, node, null, initializer.Type.Name);

            return null;
        }

        if(tuple.ElementTypes.Count != targetCount)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.DeconstructionArityMismatch, node, null,
                initializer.Type.Name, tuple.ElementTypes.Count, targetCount);

            return null;
        }

        return tuple.ElementTypes;
    }

    private static Statement BindReturn(ReturnStatementNode returnStatement, BindingContext ctx, BuilderContext context)
    {
        if(returnStatement.Value is null)
        {
            return new ReturnStatement(value: null, targetType: ctx.ReturnType);
        }

        Expression value;
        using(ctx.PushTargetType(ctx.ReturnType))
        {
            value = ExpressionBinder.BindExpression(returnStatement.Value, ctx, context);
        }

        if(!ctx.InLambda)
        {
            value = NewtypeLiterals.Admit(value, ctx.ReturnType, context.ProgramModel.TypeInterner)!;
        }

        value = ImplicitConversions.At(value, ctx.ReturnType, context.ProgramModel.TypeInterner);

        return new ReturnStatement(value, ctx.ReturnType, returnStatement.RefKeyword is not null);
    }

    private static IfStatement BindIf(IfStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var thenScope = new BodyScope(ctx.Scope);
        var condition = BindConditionInto(node.Condition, thenScope, ctx, context);
        var conditionBinds = thenScope.Locals.Count > 0;

        var thenBlock = new Block(node.ThenBranch);
        BodyBinder.BindNestedBlock(thenBlock, thenScope, ctx, context);

        Statement? elseBranch = null;
        if(node.ElseBranch is not null)
        {
            elseBranch = node.ElseBranch switch
            {
                ElseIfElseClauseNode elseIf => BindIf(elseIf.ElseIf, ctx, context),
                ElseBlockElseClauseNode elseBlock => BindNestedBlock(elseBlock.ElseBlock, ctx, context),
                _ => null,
            };
        }

        return new IfStatement(condition, thenBlock, elseBranch, conditionBinds);
    }

    private static WhileStatement BindWhile(WhileStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var bodyScope = new BodyScope(ctx.Scope);
        var condition = BindConditionInto(node.Condition, bodyScope, ctx, context);
        var conditionBinds = bodyScope.Locals.Count > 0;

        var body = new Block(node.Body);
        BodyBinder.BindNestedBlock(body, bodyScope, ctx, context);

        return new WhileStatement(condition, body, conditionBinds);
    }

    /// <summary>
    /// Binds a condition into the scope of the body it guards, which is built BEFORE it rather than
    /// after. That ordering is what a pattern's binding needs: declared as the condition binds, it
    /// reaches the rest of the condition and the guarded block, and nothing else -- not the else
    /// branch, not the statements following. Binding the condition in the enclosing scope and
    /// grafting the variable onto the block afterwards is the scheme that makes
    /// <c>x is Foo f &amp;&amp; f.Bar</c> impossible to express.
    /// </summary>
    private static Expression BindConditionInto(
        ExpressionNode node, BodyScope scope, BindingContext ctx, BuilderContext context)
    {
        var enclosing = ctx.Scope;
        ctx.Scope = scope;
        var condition = ExpressionBinder.BindExpression(node, ctx, context);
        ctx.Scope = enclosing;

        return condition;
    }

    private static ForStatement BindFor(ForStatementNode node, BindingContext ctx, BuilderContext context)
    {
        // The for loop gets its own scope so the initializer variable is contained.
        var forScope = new BodyScope(ctx.Scope);
        var previousScope = ctx.Scope;
        ctx.Scope = forScope;

        Statement? initializer = null;
        if(node.Initializer is not null)
        {
            initializer = BindForInitializer(node.Initializer, ctx, forScope, context);
        }

        Expression? condition = null;
        if(node.Condition is not null)
        {
            condition = ExpressionBinder.BindExpression(node.Condition, ctx, context);
        }

        Expression? iterator = null;
        if(node.Iterator is not null)
        {
            iterator = ExpressionBinder.BindExpression(node.Iterator, ctx, context);
        }

        var body = new Block(node.Body);
        BodyBinder.BindNestedBlock(body, new BodyScope(forScope), ctx, context);

        ctx.Scope = previousScope;
        return new ForStatement(initializer, condition, iterator, body);
    }

    private static Statement BindForInitializer(ForInitializerNode node, BindingContext ctx, BodyScope forScope, BuilderContext context)
    {
        switch(node)
        {
            case ForInitializerVariableDeclarationForInitializerNode typed:
                {
                    var declaredType = context.Resolver.Resolve(typed.VariableDeclaration, forScope);
                    var init = ExpressionBinder.BindExpression(typed.Initializer, ctx, context);
                    var local = new LocalVariable(typed.Name.Lexeme, declaredType);
                    forScope.DeclareLocal(local, context.Diagnostics);
                    return new LocalDeclarationStatement(local, init);
                }

            case ForInitializerVarDeclarationForInitializerNode varDecl:
                {
                    var init = ExpressionBinder.BindExpression(varDecl.Initializer, ctx, context);
                    var local = new LocalVariable(varDecl.Name.Lexeme, init.Type) { IsFailed = PriorFailure.Of(init) };
                    forScope.DeclareLocal(local, context.Diagnostics);
                    return new LocalDeclarationStatement(local, init);
                }

            case ForInitializerExpressionForInitializerNode expr:
                {
                    var expression = ExpressionBinder.BindExpression(expr.Expression, ctx, context);
                    return new ExpressionStatement(expression);
                }

            default:
                // The initializer node rather than the enclosing `for`, since it is the part that
                // went unbound and so the part worth pointing a diagnostic at.
                return new UnsupportedStatement(node);
        }
    }

    private static ForEachStatement BindForEach(ForeachStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var foreachScope = new BodyScope(ctx.Scope);

        IMistType variableType;
        Expression collection;
        string variableName;
        BlockNode bodyNode;
        var failed = false;

        switch(node)
        {
            case TypedForeachForeachStatementNode typed:
                variableType = context.Resolver.Resolve(typed.Type, foreachScope);
                collection = ExpressionBinder.BindExpression(typed.Collection, ctx, context);
                variableName = typed.Name.Lexeme;
                bodyNode = typed.Body;
                break;

            case VarForeachForeachStatementNode varForeach:
                collection = ExpressionBinder.BindExpression(varForeach.Collection, ctx, context);
                variableType = EnumerableElement.Of(collection.Type) ?? Model.Types.ErrorType.Instance;
                if(variableType is Model.Types.ErrorType && collection.Type is not Model.Types.ErrorType)
                {
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.ForEachElementNotInferable, varForeach.Collection, null,
                        Model.Types.TypeDisplay.Mist(collection.Type));
                }

                failed = variableType is Model.Types.ErrorType
                    && (collection.Type is not Model.Types.ErrorType || PriorFailure.Of(collection));
                variableName = varForeach.Name.Lexeme;
                bodyNode = varForeach.Body;
                break;

            default:
                return new ForEachStatement(
                    new LocalVariable("_", Model.Types.ErrorType.Instance),
                    new UnresolvedExpression(node),
                    new Block(null!));
        }

        // Read-only as C#'s iteration variable is. A loop whose element converts emits its variable as an
        // ordinary local, so Roslyn cannot be the one to refuse a reassignment.
        var variable = new LocalVariable(variableName, variableType) { IsReadonly = true, IsFailed = failed };
        foreachScope.DeclareLocal(variable, context.Diagnostics);

        var body = new Block(bodyNode);
        BodyBinder.BindNestedBlock(body, foreachScope, ctx, context);

        var conversion = EnumerableElement.Of(collection.Type) is { } element
            ? ImplicitConversions.At(new ElementReadExpression(collection.Node, element), variableType, context.ProgramModel.TypeInterner) as ImplicitConversionExpression
            : null;

        return new ForEachStatement(variable, collection, body, conversion);
    }
}