using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Resolves identifier expressions. Every kind a bare name can denote -- local, parameter,
/// type parameter, nested type, enclosing-type member, inherited member, type, namespace --
/// comes back from the one scope-chain lookup, so this binder only has to map the resolved
/// symbol onto a bound node. It does not search anywhere the lookup does not.
/// </summary>
internal static class IdentifierBinder
{
    internal static Expression BindIdentifier(IdentifierExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;

        // An expression position admits every kind of member, so the request starts wide; the
        // scopes narrow it themselves on the way out of a static member or a nested type.
        var result = ctx.Scope.Lookup(new LookupRequest(name, MemberVisibility.InstanceAndStatic, node));

        return result switch
        {
            VariableResult variable => new LocalReferenceExpression(node, variable.Variable, BodyScope.NarrowedIn(ctx.Scope, variable.Variable)),
            FieldResult field => new FieldReferenceExpression(node, field.Field),
            PropertyResult property => new PropertyReferenceExpression(node, property.Property),
            TypeResult type => new TypeReferenceExpression(node, type.Type),
            MethodGroupResult { Methods.Count: > 0 } group => new MethodGroupExpression(node, null, name, group.Methods, null),
            NamespaceResult ns => new NamespaceReferenceExpression(node, ns.NamespaceName),

            // Matched on its own rather than folded into the fallthrough below, which still covers
            // results the binder cannot represent yet -- an enum variant named bare, among them.
            // Those are the compiler being unfinished and must stay silent; this one is the program
            // being wrong.
            NotFoundResult => ReportUnresolved(node, name, context),

            _ => new UnresolvedExpression(node),
        };
    }

    private static Expression ReportUnresolved(IdentifierExpressionPrimaryNode node, string name, BuilderContext context)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedIdentifier, node, null, name);

        // ErrorExpression, not UnresolvedExpression: the name will not resolve on a later pass, and
        // the emitter must not report a second time for the same cause.
        return new ErrorExpression(node);
    }
}