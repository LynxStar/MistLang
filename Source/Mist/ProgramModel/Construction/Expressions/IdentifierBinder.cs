using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Resolves identifier expressions through the unified lookup on scopeResolver.
/// The resolver walks the scope chain (variables + types) then falls back to
/// namespace-based resolution.
/// </summary>
internal static class IdentifierBinder
{
    internal static Expression BindIdentifier(IdentifierExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;
        var result = context.Resolver.Lookup(name, ctx.Scope, node);

        switch(result)
        {
            case VariableResult variable:
                return new LocalReferenceExpression(node, variable.Variable);

            case TypeResult type:
                return new TypeReferenceExpression(node, type.Type);

            case AmbiguousResult:
                return new UnresolvedExpression(node);
        }

        // Interim: sibling method on the enclosing type. This will move into the
        // scope's lookup once methods are registered in the type scope.
        if(ctx.EnclosingType is not null)
        {
            foreach(var method in ctx.EnclosingType.MethodsList)
            {
                if(method.Name == name)
                {
                    return new MethodReferenceExpression(node, method);
                }
            }
        }

        return new UnresolvedExpression(node);
    }
}