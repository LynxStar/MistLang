using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static class KeywordTypeMemberLookup
{
    public static (Expression Receiver, MemberResolution.MemberLookup Lookup)? Resolve(
        Expression receiver, string memberName, SyntaxNode node, BindingContext ctx, BuilderContext context)
    {
        if(receiver is not TypeLiteralExpression
            {
                Node: TypeLiteralExpressionPrimaryNode
                {
                    TypeRef: { First: PrimitiveSingleTypeRefSingleTypeRefNode { ArrayRank: null, Optional: null }, SingleTypeRefs.Length: 0 },
                },
            } literal)
        {
            return null;
        }

        // A keyword remains a Type value unless its represented type answers a static member.
        var lookup = MemberResolution.Resolve(
            literal.ReferencedType, memberName, MemberVisibility.StaticOnly, node, context, ctx.Scope,
            allowExtensionProperties: true, reportMissing: false);

        if(lookup.Found is not NotFoundResult)
        {
            return (new TypeReferenceExpression(literal.Node, literal.ReferencedType), lookup);
        }

        var reflected = MemberResolution.Resolve(
            literal.Type, memberName, MemberVisibility.InstanceOnly, node, context, ctx.Scope,
            allowExtensionProperties: true, reportMissing: false);

        if(reflected.Found is not NotFoundResult)
        {
            return (literal, reflected);
        }

        return (new TypeReferenceExpression(literal.Node, literal.ReferencedType), MemberResolution.Resolve(
            literal.ReferencedType, memberName, MemberVisibility.StaticOnly, node, context, ctx.Scope,
            allowExtensionProperties: true));
    }
}