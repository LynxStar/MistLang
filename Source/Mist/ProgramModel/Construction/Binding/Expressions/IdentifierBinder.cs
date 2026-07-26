using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Resolves identifier expressions through the unified lookup on ScopeResolver.
/// The resolver walks the scope chain (variables, types, methods) then falls back
/// to namespace-based resolution.
/// </summary>
internal static class IdentifierBinder
{
    internal static Expression BindIdentifier(IdentifierExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;
        var result = context.Resolver.Lookup(name, ctx.Scope, node);

        return result switch
        {
            VariableResult variable => new LocalReferenceExpression(node, variable.Variable),
            TypeResult type => new TypeReferenceExpression(node, type.Type),
            MethodGroupResult group => new MethodReferenceExpression(node, group.Methods[0]),
            NamespaceResult ns => new NamespaceReferenceExpression(node, ns.NamespaceName),
            AmbiguousResult => new UnresolvedExpression(node),
            _ => new UnresolvedExpression(node),
        };
    }
}