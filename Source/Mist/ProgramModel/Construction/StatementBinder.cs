using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Construction;

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
            BlockStatementStatementNode nestedBlock => BindNestedBlock(nestedBlock.BlockStatement, ctx, context),
            _ => new UnsupportedStatement(statement),
        };
    }

    private static Block BindNestedBlock(BlockNode node, BindingContext ctx, BuilderContext context)
    {
        var block = new Block(node);
        BodyBinder.BindBody(block, new Scope(ctx.Scope), ctx.ReturnType, context);
        return block;
    }

    private static Statement BindVariableDeclaration(VariableDeclarationStatementStatementNode statement, BindingContext ctx, BuilderContext context)
    {
        if(statement.VariableDeclaration is not TypedVariableDeclarationVariableDeclarationStatementNode typed)
        {
            return new UnsupportedStatement(statement);
        }

        var declaredType = context.TypeResolver.Resolve(typed.Type, ctx.Scope);

        Expression? initializer = null;
        if(typed.Initializer is not null)
        {
            ctx.TargetType = declaredType;
            initializer = ExpressionBinder.BindExpression(typed.Initializer, ctx, context);
            ctx.TargetType = null;
        }

        var local = new LocalSymbol(typed.Name.Lexeme, declaredType);
        ctx.Scope.RegisterLocal(local, context.Diagnostics);
        return new LocalDeclarationStatement(local, initializer);
    }

    private static Statement BindReturn(ReturnStatementNode returnStatement, BindingContext ctx, BuilderContext context)
    {
        if(returnStatement.Value is null)
        {
            return new ReturnStatement(value: null);
        }

        ctx.TargetType = ctx.ReturnType;
        var value = ExpressionBinder.BindExpression(returnStatement.Value, ctx, context);
        ctx.TargetType = null;
        return new ReturnStatement(value);
    }
}