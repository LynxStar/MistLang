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

        // A group is born inside this walk, so this is the last point that still knows one went
        // unconsumed -- unless an enclosing call has yet to choose the overload that would consume it,
        // which is what the context flag says. Left to the emitter it would surface as
        // MethodGroupReachedEmission, which blames the compiler for what is a gap in the frontend.
        if(result is MethodGroupExpression group)
        {
            if(MethodGroupConversion.Convert(group, ctx.TargetType, context.ProgramModel.TypeInterner)
                is { } converted)
            {
                return converted;
            }

            if(ctx.CallSettlesMethodGroups)
            {
                return group;
            }

            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.MethodGroupNotConverted, group.Node, null, group.MemberName);

            return new ErrorExpression(group.Node);
        }

        return result;
    }

    /// <summary>
    /// A call that states its type arguments on an UNQUALIFIED name, which the grammar makes a
    /// primary rather than a postfix suffix -- <c>Describe&lt;[int, string]&gt;(...)</c> is one node
    /// where <c>x.Describe&lt;...&gt;(...)</c> is a receiver plus a suffix. The same group, the same
    /// arguments and the same selection either way; only the shape reaching the binder differs.
    /// </summary>
    internal static Expression BindGenericInvocation(
        GenericInvocationExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;
        var found = ctx.Scope.Lookup(new LookupRequest(name, MemberVisibility.InstanceAndStatic, node));

        if(found is not MethodGroupResult { Methods.Count: > 0 } group)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedIdentifier, node, null, name);
            return new ErrorExpression(node);
        }

        return BindInvocation(
            node,
            new MethodGroupExpression(node, null, name, group.Methods, null),
            node.Arguments,
            context.Resolver.ResolveTypeArguments(node.TypeArgs, ctx.Scope),
            ctx,
            context);
    }

    private static Expression BindPostfixSuffix(PostfixSuffixNode suffix, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        switch(suffix)
        {
            case InvocationSuffixPostfixSuffixNode invocation:
                return BindInvocation(suffix, receiver, invocation.Arguments, null, ctx, context);

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                return BindMemberAccess(suffix, receiver, memberAccess.Name.Lexeme, ctx, context);

            case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var memberExpr = BindMemberAccess(suffix, receiver, methodCall.Name.Lexeme, ctx, context);
                    var typeArguments = methodCall.TypeArgs is { } stated
                        ? context.Resolver.ResolveTypeArguments(stated, ctx.Scope)
                        : null
                        ;

                    return BindInvocation(suffix, memberExpr, methodCall.Arguments, typeArguments, ctx, context);
                }

            case OptionalMemberAccessSuffixPostfixSuffixNode optionalMemberAccess:
                return BindConditionalAccess(suffix, receiver, optionalMemberAccess.Name.Lexeme, ctx, context);

            case OptionalMethodCallSuffixPostfixSuffixNode optionalMethodCall:
                {
                    var memberExpr = BindConditionalAccess(
                        suffix, receiver, optionalMethodCall.Name.Lexeme, ctx, context);
                    var typeArguments = optionalMethodCall.TypeArgs is { } stated
                        ? context.Resolver.ResolveTypeArguments(stated, ctx.Scope)
                        : null
                        ;

                    return BindInvocation(suffix, memberExpr, optionalMethodCall.Arguments, typeArguments, ctx, context);
                }

            case IndexSuffixPostfixSuffixNode indexSuffix:
                {
                    var index = ExpressionBinder.BindExpression(indexSuffix.Index, ctx, context);
                    var resultType = ResolveElementAccessType(receiver.Type, index.Type, indexSuffix, context);
                    return new IndexExpression(suffix, receiver, index, resultType);
                }

            case SpreadSuffixPostfixSuffixNode:
                return new SpreadExpression(suffix, receiver, receiver.Type);

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
        if(receiver is not MethodGroupExpression group)
        {
            return BindFunctionInvocation(node, receiver, arguments, ctx, context);
        }

        var bound = ArgumentBinder.Bind(arguments, group.Candidates, ctx, context, group.ReceiverConstructed);
        var interner = context.ProgramModel.TypeInterner;

        var choice = OverloadResolution.Select(
            group.Candidates, bound, group.ReceiverConstructed, typeArguments, interner,
            ArgumentBinder.RefFlags(arguments));

        if(choice.Selected is not { } selected)
        {
            OverloadFailure.Report(
                choice, group.Candidates, bound,
                $"method '{group.MemberName}'", node, context.Diagnostics);

            return new ErrorExpression(node);
        }

        bound = MethodGroupConversion.Settle(
            bound, selected.ParametersList, choice.Substitution, context);

        var returnType = ClrInboundLifting.Close(
            selected.Modifiers,
            choice.Substitution.Apply(selected.ReturnType, interner),
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            interner);

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
        context.ProgramModel.VariadicUses.Record(selected, methodTypeArguments);

        var callee = group.Receiver is null
            ? new MethodReferenceExpression(node, selected, methodTypeArguments)
            : (Expression)new MemberAccessExpression(
                node, group.Receiver, group.MemberName, returnType, methodTypeArguments, group.IsConditional);

        context.ProgramModel.AwaitUses.RecordCall(ctx.EnclosingMethod, selected);

        return AutoAwait(
            new InvocationExpression(
                node, callee, OwnedProvenance.AtParameters(bound, selected.ParametersList),
                returnType, selected,
                selected.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner))),
            ctx,
            context);
    }

    /// <summary>
    /// A task-returning call in a SYNC context yields its result, with the await supplied here. The
    /// same call in an async context yields the task and the author writes the await, which is the
    /// whole of the difference between the two contexts.
    /// <para>
    /// Keyed on the call's TYPE rather than on the callee being declared async, so a method returning
    /// a task it built itself reads the same as one the transform wrapped -- there is no way to tell
    /// them apart at the call site, and no reason to.
    /// </para>
    /// </summary>
    private static Expression AutoAwait(
        InvocationExpression invocation, BindingContext ctx, BuilderContext context)
    {
        // A POSITION WANTING THE TASK suppresses the insertion as surely as an async context does:
        // `Task<int> t = f();` asks for what f returns, and unwrapping it there would answer with a
        // type the position rejected.
        if(ctx.IsAsyncContext
            || (ctx.TargetType is { } target && TaskResult.IsTask(target))
            || TaskResult.Of(invocation.Type) is not { } result)
        {
            return invocation;
        }

        context.ProgramModel.AwaitUses.RecordAwait(ctx.EnclosingMethod);

        return new AwaitExpression(invocation.Node, invocation, result) { IsImplicit = true };
    }

    /// <summary>
    /// Invoking a VALUE rather than a method group, which resolves no member and chooses no overload:
    /// the receiver's own type states what the call takes and produces. A function type therefore
    /// results in its RESULT, where anything else can only stand for itself -- the receiver's type
    /// being all there is to go on.
    /// </summary>
    private static Expression BindFunctionInvocation(
        Storm.Runtime.SyntaxNode node,
        Expression receiver,
        ArgumentListNode? arguments,
        BindingContext ctx,
        BuilderContext context)
    {
        if(receiver.Type is not IFunctionType function)
        {
            var untargeted = ArgumentBinder.Bind(arguments, ctx, context);
            return new InvocationExpression(node, receiver, untargeted, receiver.Type, null, parameterTypes: null);
        }

        var bound = ArgumentBinder.BindTargeted(arguments, function.ParameterTypes, ctx, context);

        if(bound.Count != function.ParameterTypes.Count)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.FunctionCallArityMismatch, node, null,
                function.Name, function.ParameterTypes.Count, bound.Count);

            return new ErrorExpression(node);
        }

        // Invoking a func VALUE takes the same auto-await as invoking a method: the rule is about what
        // the call produces and where it sits, and a delegate returning a task is a task-returning
        // call however it was reached.
        return AutoAwait(
            new InvocationExpression(node, receiver, bound, function.ReturnType, null, function.ParameterTypes), ctx, context);
    }

    /// <summary>
    /// Binds <c>receiver?.member</c>: the member is looked up on the receiver's non-void
    /// PORTION rather than its type, and the result is lifted back to optional, since the
    /// receiver's absence short-circuits into the result. A non-optional receiver projects to
    /// itself and binds as if the access were unconditional -- reporting that misuse is an
    /// analyzer's, as nothing stops the model building.
    /// </summary>
    private static Expression BindConditionalAccess(
        Storm.Runtime.SyntaxNode node, Expression receiver, string memberName, BindingContext ctx, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;
        var projected = OptionalLifting.NonVoidPortion(receiver.Type, interner);

        var (found, constructed) = MemberResolution.Resolve(
            projected, memberName, MemberVisibility.InstanceOnly, node, context, ctx.Scope);

        if(found is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(
                node, receiver, memberName, group.Methods, constructed, isConditional: true);
        }

        var resolved = TypeSubstitution
            .ForMember(constructed, found, interner)
            .Apply(MemberTypeOf(found), interner);

        var lifted = OptionalLifting.Lift(
            resolved, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner);

        return new MemberAccessExpression(node, receiver, memberName, lifted, isConditional: true, member: found);
    }

    private static Expression BindMemberAccess(Storm.Runtime.SyntaxNode node, Expression receiver, string memberName, BindingContext ctx, BuilderContext context)
    {
        // A namespace is not a value, so `Simpliest.Program` is one qualified name
        // rather than a member access on a receiver. Resolve the qualification instead
        // of folding it, which also keeps the namespace sentinel from surviving into
        // the finished tree.
        if(receiver is NamespaceReferenceExpression namespaceRef)
        {
            return BindQualifiedName(node, namespaceRef, memberName, context);
        }

        var (found, constructed) = ResolveMember(receiver, memberName, node, ctx, context);

        if(found is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(node, receiver, memberName, group.Methods, constructed);
        }

        // C# has no extension property, so this access is not one in the output: it becomes a call
        // to the carrier's generated getter. Reclassified here rather than at emission because the
        // expression's TYPE comes from the property either way, and a node that still said "access"
        // would have to be re-read as a call by everything downstream.
        if(found is ExtensionPropertyResult extensionProperty)
        {
            return new ExtensionPropertyAccessExpression(
                node,
                receiver,
                extensionProperty.Property,
                extensionProperty.Block.CarrierName,
                extensionProperty.Property.Type);
        }

        // DeclaredType and not every resolved type: `Length` on an array is a type PARAMETER, and it
        // reaches C# only as a member access emitting `arr.Length` -- a type reference drops the receiver.
        if(found is TypeResult { Type: DeclaredType nested })
        {
            return new TypeReferenceExpression(node, nested);
        }

        var resolved = ClrInboundLifting.Close(
            found,
            TypeSubstitution
                .ForMember(constructed, found, context.ProgramModel.TypeInterner)
                .Apply(MemberTypeOf(found), context.ProgramModel.TypeInterner),
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            context.ProgramModel.TypeInterner);

        return new MemberAccessExpression(node, receiver, memberName, resolved, member: found);
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
        Expression receiver, string memberName, Storm.Runtime.SyntaxNode node, BindingContext ctx, BuilderContext context)
    {
        // A pack named in expression position is not a type being asked for a static member: it IS
        // the comptime Type[] it ranges over, so it answers as a VALUE of its own domain. Which is
        // what makes `TArgs.Length` the ordinary array length rather than something a pack needs
        // its own rule for, and iteration and indexing come along on the same footing.
        if(receiver is TypeReferenceExpression { ReferencedType: TypeParameter { IsPack: true, Domain: { } domain } })
        {
            return MemberResolution.Resolve(
                domain, memberName, MemberVisibility.InstanceOnly, node, context, ctx.Scope,
                allowExtensionProperties: true);
        }

        var isTypeReference = receiver is TypeReferenceExpression;

        var receiverType = receiver is TypeReferenceExpression typeRef
            ? typeRef.ReferencedType
            : receiver.Type;

        var visibility = isTypeReference
            ? MemberVisibility.StaticOnly
            : MemberVisibility.InstanceOnly
            ;

        return MemberResolution.Resolve(
            receiverType, memberName, visibility, node, context, ctx.Scope, allowExtensionProperties: true);
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
    /// <c>a[x]</c> yields an element or a sub-sequence depending on what <c>x</c> IS, so the receiver
    /// alone cannot type it -- <c>numbers[1]</c> and <c>numbers[1..3]</c> differ in result type while
    /// naming the same indexer. Only the range case reports: an index that resolves nothing keeps
    /// answering <see cref="ErrorType"/> silently as it always has, where a range that resolves nothing
    /// has no C# to write and would otherwise reach emission as a slice of an error type.
    /// </summary>
    private static IMistType ResolveElementAccessType(
        IMistType receiverType,
        IMistType argumentType,
        IndexSuffixPostfixSuffixNode node,
        BuilderContext context)
    {
        if(!Indexability.IsRange(argumentType))
        {
            return Indexability.ElementType(receiverType, context);
        }

        var sliced = Indexability.SliceType(receiverType, context);

        if(sliced is ErrorType)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ReceiverNotSliceable, node, null, receiverType.Name);
        }

        return sliced;
    }
}