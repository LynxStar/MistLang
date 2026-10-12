using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static class GenericTypeMemberBinder
{
    internal static Expression BindMemberAccess(
        GenericTypeMemberAccessExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return BindMemberAccess(node, node.Type, node.TypeArgs, node.Member.Lexeme, ctx, context);
    }

    internal static Expression BindMethodCall(
        GenericTypeMethodCallExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return PostfixBinder.BindInvocation(
            node,
            BindMemberAccess(node, node.Type, node.TypeArgs, node.Member.Lexeme, ctx, context),
            node.Arguments,
            context.Resolver.ResolveTypeArguments(node.MethodTypeArgs, ctx.Scope),
            ctx,
            context);
    }

    private static Expression BindMemberAccess(
        SyntaxNode node, QualifiedIdentifierNode typeNameNode, TypeArgumentListNode stated,
        string memberName, BindingContext ctx, BuilderContext context)
    {
        var typeName = QualifiedNameHelper.Build(typeNameNode);
        var arity = 1 + stated.TypeArguments.Length;
        var definition = context.Resolver.ResolveGenericDefinition(typeName, arity, ctx.Scope, node);

        if(definition is ErrorType)
        {
            return new ErrorExpression(node);
        }

        var typeArgs = context.Resolver.BindPack(
            definition, context.Resolver.ResolveTypeArguments(stated, ctx.Scope), node);
        var enclosing = context.Resolver.InheritedEnclosing(definition, ctx.Scope);
        context.ProgramModel.ConstructorConstraintClosures.RecordStampableType(definition, typeArgs, node, enclosing, ctx.Scope);
        var type = definition is TypeAlias alias
            ? context.Resolver.ExpandAlias(alias, typeArgs, node, ctx.Scope)
            : context.Resolver.Construct(definition, typeArgs, ctx.Scope, enclosing);
        var receiver = new TypeReferenceExpression(node, type);

        if(definition is TypeAlias declared
            && declared.TypeParametersList.Find(parameter => parameter.ValueProperty?.Name == memberName) is { ValueProperty: { } projected })
        {
            return new MemberAccessExpression(
                node, receiver, projected.Name, projected.Type, member: new PropertyResult(projected, declared, [new ConstructedType(declared, typeArgs)]));
        }

        return PostfixBinder.BindMemberAccess(node, receiver, memberName, ctx, context);
    }
}