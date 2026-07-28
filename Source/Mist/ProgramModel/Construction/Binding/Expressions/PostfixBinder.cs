using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Handles postfix expressions: binds the primary target, then folds each
/// suffix (member access, invocation, method call) onto the result.
/// </summary>
internal static class PostfixBinder
{
    internal static Expression BindPostfix(PostfixNode node, BindingContext ctx, BuilderContext context)
    {
        var result = ExpressionBinder.BindPrimary(node.Target, ctx, context);

        foreach(var suffix in node.Suffix)
        {
            result = BindPostfixSuffix(suffix, result, ctx, context);
        }

        return result;
    }

    private static Expression BindPostfixSuffix(PostfixSuffixNode suffix, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        switch(suffix)
        {
            case InvocationSuffixPostfixSuffixNode invocation:
                return BindInvocation(suffix, receiver, invocation.Arguments, ctx, context);

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                return BindMemberAccess(suffix, receiver, memberAccess.Name.Lexeme, context);

            case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var memberExpr = BindMemberAccess(suffix, receiver, methodCall.Name.Lexeme, context);
                    return BindInvocation(suffix, memberExpr, methodCall.Arguments, ctx, context);
                }

            case IndexSuffixPostfixSuffixNode indexSuffix:
                {
                    var index = ExpressionBinder.BindExpression(indexSuffix.Index, ctx, context);
                    var resultType = ResolveIndexerType(receiver.Type);
                    return new IndexExpression(suffix, receiver, index, resultType);
                }

            default:
                return new UnresolvedExpression(suffix);
        }
    }

    private static InvocationExpression BindInvocation(Storm.Runtime.SyntaxNode node, Expression receiver, ArgumentListNode? arguments, BindingContext ctx, BuilderContext context)
    {
        var bound = ArgumentBinder.Bind(arguments, ctx, context);
        return new InvocationExpression(node, receiver, bound, receiver.Type);
    }

    private static Expression BindMemberAccess(Storm.Runtime.SyntaxNode node, Expression receiver, string memberName, BuilderContext context)
    {
        // A namespace is not a value, so `Simpliest.Program` is one qualified name
        // rather than a member access on a receiver. Resolve the qualification instead
        // of folding it, which also keeps the namespace sentinel from surviving into
        // the finished tree.
        if(receiver is NamespaceReferenceExpression namespaceRef)
        {
            return BindQualifiedName(node, namespaceRef, memberName, context);
        }

        var memberType = ResolveMemberType(receiver, memberName);
        return new MemberAccessExpression(node, receiver, memberName, memberType);
    }

    /// <summary>
    /// Resolves one qualification step off a namespace receiver. A namespace can only
    /// contain types and further namespaces, so the result collapses the receiver
    /// rather than nesting onto it.
    /// </summary>
    private static Expression BindQualifiedName(Storm.Runtime.SyntaxNode node, NamespaceReferenceExpression namespaceRef, string memberName, BuilderContext context)
    {
        var result = context.Resolver.LookupQualified(namespaceRef.NamespaceName, memberName);

        return result switch
        {
            TypeResult type => new TypeReferenceExpression(node, type.Type),
            NamespaceResult ns => new NamespaceReferenceExpression(node, ns.NamespaceName),
            _ => new UnresolvedExpression(node),
        };
    }

    /// <summary>
    /// Resolves the type of a member accessed on a receiver. Looks up methods and
    /// properties on the receiver's type. Falls back to the error type for members
    /// that cannot be resolved yet.
    /// </summary>
    private static IMistType ResolveMemberType(Expression receiver, string memberName)
    {
        // If the receiver is a type reference (static access like Console.WriteLine),
        // look up the member on that type.
        var lookupType = receiver is TypeReferenceExpression typeRef
            ? typeRef.ReferencedType
            : receiver.Type;

        if(lookupType is MemberType memberType)
        {
            foreach(var method in memberType.MethodsList)
            {
                if(method.Name == memberName)
                {
                    return method.ReturnType;
                }
            }

            foreach(var property in memberType.PropertiesList)
            {
                if(property.Name == memberName)
                {
                    return property.Type;
                }
            }

            // Fields last: only concrete types declare them, and a name that is both a
            // method and a field cannot occur.
            if(memberType is ConcreteType concrete)
            {
                foreach(var field in concrete.FieldsList)
                {
                    if(field.Name == memberName)
                    {
                        return field.Type;
                    }
                }
            }
        }

        return ErrorType.Instance;
    }

    /// <summary>
    /// Resolves the return type of the indexer on the receiver type.
    /// For constructed types, looks up the indexer on the definition type and
    /// substitutes type parameters against the constructed type's arguments.
    /// </summary>
    private static IMistType ResolveIndexerType(IMistType receiverType)
    {
        var lookupType = receiverType is ConstructedType constructed
            ? constructed.Definition
            : receiverType
            ;

        if(lookupType is MemberType memberType && memberType.IndexersList.Count > 0)
        {
            var indexer = memberType.IndexersList[0];

            if(receiverType is ConstructedType ct)
            {
                return ct.Substitute(indexer.Type);
            }

            return indexer.Type;
        }

        return ErrorType.Instance;
    }
}