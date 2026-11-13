using Mist.Common.Diagnostics;
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
        var (member, captures) = BindMemberAccess(node, node.Type, node.TypeArgs, node.Member.Lexeme, ctx, context);

        return Attached(member, captures, node, context);
    }

    internal static Expression BindCallee(
        GenericTypeMemberAccessExpressionPrimaryNode node, BindingContext ctx, BuilderContext context, out IReadOnlyList<SlotCapture> owed)
    {
        var (member, captures) = BindMemberAccess(node, node.Type, node.TypeArgs, node.Member.Lexeme, ctx, context);
        owed = member is MethodGroupExpression ? captures : [];

        return member is MethodGroupExpression ? member : Attached(member, captures, node, context);
    }

    private static Expression Attached(Expression member, IReadOnlyList<SlotCapture> captures, SyntaxNode node, BuilderContext context)
    {
        return member is MemberAccessExpression access && captures.Count > 0
            ? new MemberAccessExpression(access.Node, access.Receiver, access.MemberName, access.Type, access.TypeArguments, access.IsConditional, access.Member, access.IsFailed)
            {
                Captures = captures,
            }
            : Owned(member, captures, node, context);
    }

    internal static Expression BindMethodCall(
        GenericTypeMethodCallExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var (member, captures) = BindMemberAccess(node, node.Type, node.TypeArgs, node.Member.Lexeme, ctx, context);
        var stated = StatedMethodArguments.Bind(member, node.MethodTypeArgs, node.Member.Lexeme, node, ctx, context);

        return PostfixBinder.BindInvocation(
            node,
            stated,
            node.Arguments,
            (stated as MethodGroupExpression)?.StatedTypeArguments,
            ctx,
            context,
            captures: captures);
    }

    /// <summary>
    /// A capture belongs to the node that reads it. A member access and a call own theirs; anything else
    /// reached through the closing, a nested type among them, cannot, and the value's name is refused as
    /// it is outside a body.
    /// </summary>
    private static Expression Owned(Expression member, IReadOnlyList<SlotCapture> captures, SyntaxNode node, BuilderContext context)
    {
        if(captures.Count > 0 && member is not ErrorExpression)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.SlotValueOutsideBody, node, null, new NameValue(captures[0].Name));
            return new ErrorExpression(node);
        }

        return member;
    }

    private static (Expression Member, IReadOnlyList<SlotCapture> Captures) BindMemberAccess(
        SyntaxNode node, QualifiedIdentifierNode typeNameNode, TypeArgumentListNode stated,
        string memberName, BindingContext ctx, BuilderContext context)
    {
        var typeName = QualifiedNameHelper.Build(typeNameNode);
        var arity = 1 + stated.TypeArguments.Length;
        var definition = context.Resolver.ResolveGenericDefinition(typeName, arity, ctx.Scope, node);

        if(definition is ErrorType)
        {
            return (new ErrorExpression(node), []);
        }

        var values = new SlotValueBinder(ctx, context);
        var typeArgs = context.Resolver.BindPack(
            definition, context.Resolver.ResolveTypeArguments(stated, ctx.Scope, definition, values), node);
        var enclosing = context.Resolver.InheritedEnclosing(definition, ctx.Scope);
        context.ProgramModel.ConstructorConstraintClosures.RecordStampableType(definition, typeArgs, node, enclosing, ctx.Scope);
        var type = definition is TypeAlias alias
            ? context.Resolver.ExpandAlias(alias, typeArgs, node, ctx.Scope)
            : context.Resolver.Construct(definition, typeArgs, ctx.Scope, enclosing);
        var receiver = new TypeReferenceExpression(node, type);

        if(definition is TypeAlias declared
            && declared.TypeParametersList.Find(parameter => parameter.ValueProperty?.Name == memberName) is { ValueProperty: { } projected })
        {
            return (new MemberAccessExpression(
                node, receiver, projected.Name, projected.Type, member: new PropertyResult(projected, declared, [new ConstructedType(declared, typeArgs)]))
            {
                Captures = values.Captures,
            }, []);
        }

        return (PostfixBinder.BindMemberAccess(node, receiver, memberName, ctx, context), values.Captures);
    }
}