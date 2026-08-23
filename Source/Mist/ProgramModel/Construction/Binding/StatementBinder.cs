using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

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
            _ => new UnsupportedStatement(statement),
        };
    }

    private static Block BindNestedBlock(BlockNode node, BindingContext ctx, BuilderContext context)
    {
        var block = new Block(node);
        BodyBinder.BindNestedBlock(block, new BodyScope(ctx.Scope), ctx, context);
        return block;
    }

    private static ExpressionStatement BindExpressionStatement(ExpressionStatementStatementNode statement, BindingContext ctx, BuilderContext context)
    {
        var expression = ExpressionBinder.BindExpression(statement.ExpressionStatement.Expression, ctx, context);
        return new ExpressionStatement(expression);
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
                    }

                    var local = new LocalVariable(typed.Name.Lexeme, declaredType);
                    ctx.Scope.DeclareLocal(local, context.Diagnostics);
                    return new LocalDeclarationStatement(local, initializer);
                }

            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                {
                    var initializer = ExpressionBinder.BindExpression(inferred.Initializer, ctx, context);
                    var inferredType = initializer.Type;
                    var local = new LocalVariable(inferred.Name.Lexeme, inferredType);
                    ctx.Scope.DeclareLocal(local, context.Diagnostics);
                    return new LocalDeclarationStatement(local, initializer);
                }

            default:
                return new UnsupportedStatement(statement);
        }
    }

    private static Statement BindReturn(ReturnStatementNode returnStatement, BindingContext ctx, BuilderContext context)
    {
        if(returnStatement.Value is null)
        {
            return new ReturnStatement(value: null);
        }

        Expression value;
        using(ctx.PushTargetType(ctx.ReturnType))
        {
            value = ExpressionBinder.BindExpression(returnStatement.Value, ctx, context);
        }

        return new ReturnStatement(value);
    }

    private static IfStatement BindIf(IfStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var thenScope = new BodyScope(ctx.Scope);
        var condition = BindConditionInto(node.Condition, thenScope, ctx, context);

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

        return new IfStatement(condition, thenBlock, elseBranch);
    }

    private static WhileStatement BindWhile(WhileStatementNode node, BindingContext ctx, BuilderContext context)
    {
        var bodyScope = new BodyScope(ctx.Scope);
        var condition = BindConditionInto(node.Condition, bodyScope, ctx, context);

        var body = new Block(node.Body);
        BodyBinder.BindNestedBlock(body, bodyScope, ctx, context);

        return new WhileStatement(condition, body);
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
                    var local = new LocalVariable(varDecl.Name.Lexeme, init.Type);
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
                variableType = Model.Types.ErrorType.Instance;
                variableName = varForeach.Name.Lexeme;
                bodyNode = varForeach.Body;
                break;

            default:
                return new ForEachStatement(
                    new LocalVariable("_", Model.Types.ErrorType.Instance),
                    new UnresolvedExpression(node),
                    new Block(null!));
        }

        var variable = new LocalVariable(variableName, variableType);
        foreachScope.DeclareLocal(variable, context.Diagnostics);

        var body = new Block(bodyNode);
        BodyBinder.BindNestedBlock(body, foreachScope, ctx, context);

        return new ForEachStatement(variable, collection, body);
    }
}