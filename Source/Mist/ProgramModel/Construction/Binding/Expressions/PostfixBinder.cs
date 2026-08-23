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

        // A group is born inside this walk and has to be consumed inside it, so this is the last
        // point that still knows one went unconsumed. Left to the emitter it would surface as
        // MethodGroupReachedEmission, which blames the compiler for what is a gap in the frontend.
        if(result is MethodGroupExpression group)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.MethodGroupNotConverted, group.Node, null, group.MemberName);

            return new ErrorExpression(group.Node);
        }

        return result;
    }

    private static Expression BindPostfixSuffix(PostfixSuffixNode suffix, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        switch(suffix)
        {
            case InvocationSuffixPostfixSuffixNode invocation:
                return BindInvocation(suffix, receiver, invocation.Arguments, null, ctx, context);

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                return BindMemberAccess(suffix, receiver, memberAccess.Name.Lexeme, context);

            case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var memberExpr = BindMemberAccess(suffix, receiver, methodCall.Name.Lexeme, context);
                    var typeArguments = methodCall.TypeArgs is { } stated
                        ? context.Resolver.ResolveTypeArguments(stated, ctx.Scope)
                        : null
                        ;

                    return BindInvocation(suffix, memberExpr, methodCall.Arguments, typeArguments, ctx, context);
                }

            case OptionalMemberAccessSuffixPostfixSuffixNode optionalMemberAccess:
                return BindConditionalAccess(suffix, receiver, optionalMemberAccess.Name.Lexeme, context);

            case OptionalMethodCallSuffixPostfixSuffixNode optionalMethodCall:
                {
                    var memberExpr = BindConditionalAccess(
                        suffix, receiver, optionalMethodCall.Name.Lexeme, context);
                    var typeArguments = optionalMethodCall.TypeArgs is { } stated
                        ? context.Resolver.ResolveTypeArguments(stated, ctx.Scope)
                        : null
                        ;

                    return BindInvocation(suffix, memberExpr, optionalMethodCall.Arguments, typeArguments, ctx, context);
                }

            case IndexSuffixPostfixSuffixNode indexSuffix:
                {
                    var index = ExpressionBinder.BindExpression(indexSuffix.Index, ctx, context);
                    var resultType = ResolveIndexerType(receiver.Type, context);
                    return new IndexExpression(suffix, receiver, index, resultType);
                }

            case SwitchExpressionSuffixPostfixSuffixNode switchSuffix:
                return BindSwitch(switchSuffix, receiver, ctx, context);

            default:
                return new UnresolvedExpression(suffix);
        }
    }

    private static Expression BindSwitch(
        SwitchExpressionSuffixPostfixSuffixNode node, Expression scrutinee, BindingContext ctx, BuilderContext context)
    {
        if(node.Arms is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.SwitchExpressionNeedsAnArm, node, null);
            return new ErrorExpression(node);
        }

        var armNodes = new List<SwitchArmNode>(1 + node.Arms.SwitchArms.Length) { node.Arms.SwitchArm };
        armNodes.AddRange(node.Arms.SwitchArms);

        var arms = new List<SwitchArm>(armNodes.Count);
        foreach(var armNode in armNodes)
        {
            if(BindArm(armNode, scrutinee.Type, ctx, context) is not { } arm)
            {
                return new UnresolvedExpression(node);
            }

            arms.Add(arm);
        }

        var resultType = ctx.TargetType ?? CommonArmType(arms, node, context);

        return resultType is ErrorType
            ? new ErrorExpression(node)
            : new SwitchExpression(node, scrutinee, arms, resultType)
            ;
    }

    /// <summary>
    /// Binds one arm inside a scope of its own, so the bindings its pattern introduces reach that arm's
    /// guard and result and nothing else -- not a sibling arm, and not the expression around the switch.
    /// The same ordering <see cref="StatementBinder"/> uses for a branch: the scope exists before the
    /// pattern binds into it.
    /// </summary>
    private static SwitchArm? BindArm(
        SwitchArmNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        var enclosing = ctx.Scope;
        ctx.Scope = new BodyScope(enclosing);
        var arm = BindArmParts(node, scrutineeType, ctx, context);
        ctx.Scope = enclosing;

        return arm;
    }

    private static SwitchArm? BindArmParts(
        SwitchArmNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context)
    {
        if(PatternBinder.Bind(node.Pattern, scrutineeType, ctx, context) is not { } pattern)
        {
            return null;
        }

        Expression? guard = null;
        if(node.Guard is { } when)
        {
            using(ctx.PushTargetType(null))
            {
                guard = OperatorBinder.BindVoidCoalescing(when.Condition, ctx, context);
            }
        }

        return new SwitchArm(node, pattern, guard, ExpressionBinder.BindExpression(node.Result, ctx, context));
    }

    private static IMistType CommonArmType(
        IReadOnlyList<SwitchArm> arms, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        IMistType? candidate = null;

        foreach(var arm in arms)
        {
            // An arm whose result failed to bind was reported where it failed, and pairing it with a
            // sound arm would report that same failure again as a disagreement between them.
            if(arm.Result.Type is ErrorType)
            {
                continue;
            }

            if(candidate is null)
            {
                candidate = arm.Result.Type;
                continue;
            }

            if(!ReferenceEquals(arm.Result.Type, candidate))
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.SwitchArmsNoCommonType, node, null,
                    candidate.Name, arm.Result.Type.Name);

                return ErrorType.Instance;
            }
        }

        return candidate ?? ErrorType.Instance;
    }

    /// <summary>
    /// Binds a call, selecting the overload its arguments pick out. The arguments are bound first
    /// because selection reads their types.
    /// </summary>
    private static Expression BindInvocation(
        Storm.Runtime.SyntaxNode node,
        Expression receiver,
        ArgumentListNode? arguments,
        IReadOnlyList<IMistType>? typeArguments,
        BindingContext ctx,
        BuilderContext context)
    {
        // Invoking a delegate or func value resolves no member, so the receiver's own type stands as
        // the result -- which is wrong for a delegate, whose result is its return type.
        if(receiver is not MethodGroupExpression group)
        {
            var value = ArgumentBinder.Bind(arguments, ctx, context);
            return new InvocationExpression(node, receiver, value, receiver.Type, null);
        }

        var bound = ArgumentBinder.Bind(arguments, group.Candidates, ctx, context, group.ReceiverConstructed);
        var interner = context.ProgramModel.TypeInterner;

        var choice = OverloadResolution.Select(
            group.Candidates, bound, group.ReceiverConstructed, typeArguments, interner);

        if(choice.Selected is not { } selected)
        {
            OverloadFailure.Report(
                choice, group.Candidates, bound.Count,
                $"method '{group.MemberName}'", node, context.Diagnostics);

            return new ErrorExpression(node);
        }

        var returnType = choice.Substitution.Apply(selected.ReturnType, interner);

        if(group.IsConditional)
        {
            returnType = OptionalLifting.Lift(
                returnType, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner);
        }

        // The receiver's type arguments are already spelled in the receiver expression, and
        // repeating them on the member would be C# for something else entirely.
        var methodTypeArguments = selected.TypeParametersList
            .ConvertAll(parameter => choice.Substitution.Apply(parameter, interner));

        context.ProgramModel.ConstructorConstraintClosures.RecordMethod(selected, methodTypeArguments, node);

        var callee = group.Receiver is null
            ? new MethodReferenceExpression(node, selected, methodTypeArguments)
            : (Expression)new MemberAccessExpression(
                node, group.Receiver, group.MemberName, returnType, methodTypeArguments, group.IsConditional);

        return new InvocationExpression(node, callee, bound, returnType, selected);
    }

    /// <summary>
    /// Binds <c>receiver?.member</c>: the member is looked up on the receiver's non-void
    /// PORTION rather than its type, and the result is lifted back to optional, since the
    /// receiver's absence short-circuits into the result. A non-optional receiver projects to
    /// itself and binds as if the access were unconditional -- reporting that misuse is an
    /// analyzer's, as nothing stops the model building.
    /// </summary>
    private static Expression BindConditionalAccess(
        Storm.Runtime.SyntaxNode node, Expression receiver, string memberName, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;
        var projected = OptionalLifting.NonVoidPortion(receiver.Type, interner);

        var (found, constructed) = MemberResolution.Resolve(
            projected, memberName, MemberVisibility.InstanceOnly, node, context);

        if(found is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(
                node, receiver, memberName, group.Methods, constructed, isConditional: true);
        }

        var resolved = TypeSubstitution
            .ForReceiver(constructed)
            .Apply(MemberTypeOf(found), interner);

        var lifted = OptionalLifting.Lift(
            resolved, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner);

        return new MemberAccessExpression(node, receiver, memberName, lifted, isConditional: true);
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

        var (found, constructed) = ResolveMember(receiver, memberName, node, context);

        if(found is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(node, receiver, memberName, group.Methods, constructed);
        }

        // DeclaredType and not every resolved type: `Length` on an array is a type PARAMETER, and it
        // reaches C# only as a member access emitting `arr.Length` -- a type reference drops the receiver.
        if(found is TypeResult { Type: DeclaredType nested })
        {
            return new TypeReferenceExpression(node, nested);
        }

        var resolved = TypeSubstitution
            .ForReceiver(constructed)
            .Apply(MemberTypeOf(found), context.ProgramModel.TypeInterner);

        return new MemberAccessExpression(node, receiver, memberName, resolved);
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
    /// Reduces a receiver to the type and the visibility to resolve against, and refuses to look
    /// anywhere else: the receiver names the type, so a miss is a miss rather than a reason to widen
    /// the search.
    /// <para>
    /// Naming the type reaches its statics; supplying a value reaches its instance members. C#
    /// rejects the crossings in both directions, and separating them here is what lets the lookup
    /// exclude the wrong ones rather than resolve and then reject them.
    /// </para>
    /// </summary>
    private static (LookupResult Found, ConstructedType? Constructed) ResolveMember(
        Expression receiver, string memberName, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        var isTypeReference = receiver is TypeReferenceExpression;

        var receiverType = receiver is TypeReferenceExpression typeRef
            ? typeRef.ReferencedType
            : receiver.Type;

        var visibility = isTypeReference
            ? MemberVisibility.StaticOnly
            : MemberVisibility.InstanceOnly
            ;

        return MemberResolution.Resolve(receiverType, memberName, visibility, node, context);
    }

    /// <summary>
    /// The type a resolved member contributes to the expression. No method-group arm, because a
    /// group has no single type to contribute and is intercepted before this is reached.
    /// </summary>
    private static IMistType MemberTypeOf(LookupResult found)
    {
        return found switch
        {
            FieldResult field => field.Field.Type,
            PropertyResult property => property.Property.Type,
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
    private static IMistType ResolveIndexerType(IMistType receiverType, BuilderContext context)
    {
        var constructed = receiverType as ConstructedType;
        var lookupType = constructed?.Definition ?? receiverType;

        if(lookupType is not MemberType { IndexersList.Count: > 0 } memberType)
        {
            return ErrorType.Instance;
        }

        return TypeSubstitution
            .ForReceiver(constructed)
            .Apply(memberType.IndexersList[0].Type, context.ProgramModel.TypeInterner);
    }
}