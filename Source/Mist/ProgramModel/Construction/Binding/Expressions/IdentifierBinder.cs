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
    internal static Expression BindIdentifier(IdentifierExpressionPrimaryNode node, BindingContext ctx)
    {
        var name = node.Name.Lexeme;

        // An expression position admits every kind of member, so the request starts wide; the
        // scopes narrow it themselves on the way out of a static member or a nested type.
        var result = ctx.Scope.Lookup(new LookupRequest(name, MemberVisibility.InstanceAndStatic, node));

        return result switch
        {
            VariableResult variable => new LocalReferenceExpression(node, variable.Variable),
            FieldResult field => new FieldReferenceExpression(node, field.Field),
            TypeResult type => new TypeReferenceExpression(node, type.Type),
            MethodGroupResult group => new MethodReferenceExpression(node, group.Methods[0]),
            NamespaceResult ns => new NamespaceReferenceExpression(node, ns.NamespaceName),

            // Resolution succeeds for a bare property name, but there is no bound node to
            // carry it yet -- reading a property through an implicit receiver is a separate
            // capability from resolving the name to it. Sentinel rather than diagnostic: the
            // program is fine, the binder is not finished.
            PropertyResult => new UnresolvedExpression(node),

            _ => new UnresolvedExpression(node),
        };
    }
}