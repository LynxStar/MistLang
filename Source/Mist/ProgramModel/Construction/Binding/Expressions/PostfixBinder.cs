using Mist.Common.Diagnostics;
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

        var memberType = ResolveMemberType(receiver, memberName, node, context);
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
    /// Resolves the type of a member accessed on a receiver, through the same lookup an
    /// unqualified name uses -- so a member reached as <c>receiver.X</c> and the same member
    /// reached as a bare <c>X</c> inside the type cannot disagree about what exists, what is
    /// inherited, or what is reachable.
    /// <para>
    /// The one thing this path does differently is refuse to look anywhere else: the receiver
    /// names the type, so a miss is a miss rather than a reason to widen the search.
    /// </para>
    /// </summary>
    private static IMistType ResolveMemberType(
        Expression receiver, string memberName, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        // Naming the type reaches its statics; supplying a value reaches its instance members.
        // C# rejects the crossings in both directions, and separating them here is what lets the
        // lookup exclude the wrong ones rather than resolve and then reject them.
        var isTypeReference = receiver is TypeReferenceExpression;

        var receiverType = receiver is TypeReferenceExpression typeRef
            ? typeRef.ReferencedType
            : receiver.Type;

        var visibility = isTypeReference
            ? MemberVisibility.StaticOnly
            : MemberVisibility.InstanceOnly
            ;

        // Members are declared on the generic DEFINITION, so that is what holds the answer; the
        // constructed type supplies the arguments to substitute into it.
        var constructed = receiverType as ConstructedType;
        var lookupTarget = constructed?.Definition ?? receiverType;

        if(lookupTarget is not MistType modelType)
        {
            return ErrorType.Instance;
        }

        var found = TypeMemberLookup.Find(modelType, LookupRequest.Member(memberName, visibility, node));

        Report(found, modelType, memberName, node, context);

        var memberType = MemberTypeOf(found);

        return constructed is not null
            ? constructed.Substitute(memberType)
            : memberType
            ;
    }

    /// <summary>
    /// Reports a member access that resolved to nothing, or to more than one kind of thing.
    /// <para>
    /// Suppressed when the receiver's type is itself unresolved. A receiver that already failed has
    /// already been reported on, and it has no members to speak of, so every access through it would
    /// otherwise add a second error saying so -- one mistake, a cascade of complaints.
    /// </para>
    /// </summary>
    private static void Report(
        LookupResult found,
        MistType receiverType,
        string memberName,
        Storm.Runtime.SyntaxNode node,
        BuilderContext context)
    {
        if(receiverType is ErrorType or UnknownType or NotProjectedType)
        {
            return;
        }

        switch(found)
        {
            case NotFoundResult:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.UnresolvedMember, node, null, receiverType.Name, memberName);
                break;

            case AmbiguousResult ambiguous:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.AmbiguousMember, node, null,
                    receiverType.Name, memberName, ambiguous.Candidates.Count);
                break;
        }
    }

    /// <summary>
    /// The type a resolved member contributes to the expression. A method group yields its first
    /// overload's return type: the group travels no further than here, because an invocation reads
    /// the type off its receiver, and picking between overloads by argument is not implemented.
    /// </summary>
    private static IMistType MemberTypeOf(LookupResult found)
    {
        return found switch
        {
            FieldResult field => field.Field.Type,
            PropertyResult property => property.Property.Type,
            MethodGroupResult { Methods.Count: > 0 } group => group.Methods[0].ReturnType,
            EnumMemberResult member => member.DeclaringType,
            TypeResult type => type.Type,
            _ => ErrorType.Instance,
        };
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