using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

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

        // A bare name that no scope declares may still be a field of the enclosing type,
        // accessed through an implicit this. Fields are not registered in the scope chain,
        // so locals and parameters shadow them by construction, which is the intended
        // precedence.
        if(result is NotFoundResult && FindEnclosingField(name, ctx) is { } field)
        {
            return new FieldReferenceExpression(node, field);
        }

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

    /// <summary>
    /// Finds a field by name on the enclosing type. Only concrete types declare fields,
    /// and only the type itself is searched -- inherited fields need the base chain, which
    /// arrives with the inheritance work.
    /// </summary>
    private static Field? FindEnclosingField(string name, BindingContext ctx)
    {
        if(ctx.EnclosingType is not ConcreteType concrete)
        {
            return null;
        }

        foreach(var field in concrete.FieldsList)
        {
            if(field.Name == name)
            {
                return field;
            }
        }

        return null;
    }
}