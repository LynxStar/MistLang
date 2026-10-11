using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
        Expression result;
        using(ctx.PushTargetType(node.Suffix.Length == 0 ? ctx.TargetType : null, node.Suffix.Length == 0 && ctx.TargetTypeIsFailed))
        {
            result = ExpressionBinder.BindPrimary(node.Target, ctx, context);
        }

        for(var i = 0; i < node.Suffix.Length; i++)
        {
            using(ctx.PushTargetType(i == node.Suffix.Length - 1 ? ctx.TargetType : null, i == node.Suffix.Length - 1 && ctx.TargetTypeIsFailed))
            {
                result = BindPostfixSuffix(node.Suffix[i], result, ctx, context);
            }
        }

        // A group is born inside this walk, so this is the last point that still knows one went
        // unconsumed -- unless an enclosing call has yet to choose the overload that would consume it,
        // which is what the context's settled postfix says: this one, or the one inside these
        // parentheses, the only way a group can leave a nested postfix unconverted. Left to the emitter it
        // would surface as MethodGroupReachedEmission, which blames the compiler for what is a gap in the
        // frontend.
        if(result is MethodGroupExpression group)
        {
            if(group.Candidates.Count > 0 && group.Candidates.All(LocalFunctionBinder.IsRejected))
            {
                return new ErrorExpression(group.Node, [group]);
            }

            if(ReferenceEquals(ctx.SettledMethodGroup, node) || node is { Suffix.Length: 0, Target: ParenthesizedExpressionPrimaryNode })
            {
                return group;
            }

            if(MethodGroupConversion.Convert(group, ctx.TargetType, context, out var ambiguous, ctx.Scope) is { } converted)
            {
                return converted;
            }

            context.Diagnostics.ReportAtNode(
                ambiguous ? ProgramModelDiagnostics.MethodGroupAmbiguous : ProgramModelDiagnostics.MethodGroupNotConverted,
                group.Node, null, group.MemberName);

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
            new MethodGroupExpression(node, null, name, group.Methods, null) { Paths = group.Paths },
            node.Arguments,
            context.Resolver.ResolveTypeArguments(node.TypeArgs, ctx.Scope),
            ctx,
            context);
    }

    internal static Expression BindGenericTypeMemberAccess(
        GenericTypeMemberAccessExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var typeName = QualifiedNameHelper.Build(node.Type);
        var arity = 1 + node.TypeArgs.TypeArguments.Length;
        var definition = context.Resolver.ResolveGenericDefinition(typeName, arity, ctx.Scope, node);

        if(definition is ErrorType)
        {
            return new ErrorExpression(node);
        }

        var typeArgs = context.Resolver.BindPack(
            definition, context.Resolver.ResolveTypeArguments(node.TypeArgs, ctx.Scope), node);
        var enclosing = context.Resolver.InheritedEnclosing(definition, ctx.Scope);
        context.ProgramModel.ConstructorConstraintClosures.RecordStampableType(definition, typeArgs, node, enclosing, ctx.Scope);
        var type = definition is TypeAlias alias
            ? context.Resolver.ExpandAlias(alias, typeArgs, node, ctx.Scope)
            : context.Resolver.Construct(definition, typeArgs, ctx.Scope, enclosing);

        return BindMemberAccess(node, new TypeReferenceExpression(node, type), node.Member.Lexeme, ctx, context);
    }

    /// <summary>
    /// A name with type arguments and no call after it, which the grammar admits only where an expression
    /// ends. A method is the group with its arguments stated, left for the position's function type to
    /// convert; anything else so written can only be a constructed type named as a value.
    /// </summary>
    internal static Expression BindGenericName(
        GenericNameExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var name = node.Name.Lexeme;

        if(ctx.Scope.Lookup(new LookupRequest(name, MemberVisibility.InstanceAndStatic, node)) is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(node, null, name, group.Methods, null)
            {
                Paths = group.Paths,
                StatedTypeArguments = context.Resolver.ResolveTypeArguments(node.TypeArgs, ctx.Scope),
            };
        }

        var definition = context.Resolver.ResolveGenericDefinition(name, 1 + node.TypeArgs.TypeArguments.Length, ctx.Scope, node);

        if(definition is ErrorType)
        {
            return new ErrorExpression(node);
        }

        var typeArgs = context.Resolver.BindPack(definition, context.Resolver.ResolveTypeArguments(node.TypeArgs, ctx.Scope), node);
        var enclosing = context.Resolver.InheritedEnclosing(definition, ctx.Scope);
        context.ProgramModel.ConstructorConstraintClosures.RecordStampableType(definition, typeArgs, node, enclosing, ctx.Scope);
        var type = definition is TypeAlias alias
            ? context.Resolver.ExpandAlias(alias, typeArgs, node, ctx.Scope)
            : context.Resolver.Construct(definition, typeArgs, ctx.Scope, enclosing);

        return new TypeLiteralExpression(node, type, context.Resolver.LookupWellKnown(WellKnownTypes.Type));
    }

    private static Expression WithStatedTypeArguments(
        Expression member, TypeArgumentListNode stated, string name, PostfixSuffixNode node, BindingContext ctx, BuilderContext context)
    {
        if(member is ErrorExpression)
        {
            return member;
        }

        if(member is not MethodGroupExpression group)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.TypeArgumentsOnANonMethod, node, null, name);
            return new ErrorExpression(node);
        }

        return new MethodGroupExpression(group.Node, group.Receiver, group.MemberName, group.Candidates, group.ReceiverConstructed, group.IsConditional)
        {
            Paths = group.Paths,
            StatedTypeArguments = context.Resolver.ResolveTypeArguments(stated, ctx.Scope),
        };
    }

    private static Expression BindPostfixSuffix(PostfixSuffixNode suffix, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        switch(suffix)
        {
            case GenericMethodGroupSuffixPostfixSuffixNode stated:
                return WithStatedTypeArguments(
                    BindMemberAccess(suffix, receiver, stated.Name.Lexeme, ctx, context), stated.TypeArgs, stated.Name.Lexeme, suffix, ctx, context);

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
                return IndexBinder.Bind(indexSuffix, receiver, ctx, context);

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
            if(BindArm(armNode, scrutinee.Type, ctx, context, PriorFailure.Of(scrutinee)) is not { } arm)
            {
                return new UnresolvedExpression(node);
            }

            arms.Add(arm);
        }

        var resultType = ctx.TargetType ?? CommonArmType(arms, node, context);

        if(resultType is null || arms.All(arm => PriorFailure.Of(arm.Result)))
        {
            return new ErrorExpression(node, [new SwitchExpression(node, scrutinee, arms, resultType ?? ErrorType.Instance)]);
        }

        var converted = arms.ConvertAll(arm => new SwitchArm(
            arm.Node, arm.Pattern, arm.Guard, ImplicitConversions.At(arm.Result, resultType, context.ProgramModel.TypeInterner)));

        return new SwitchExpression(node, scrutinee, converted, resultType);
    }

    /// <summary>
    /// Binds one arm inside a scope of its own, so the bindings its pattern introduces reach that arm's
    /// guard and result and nothing else -- not a sibling arm, and not the expression around the switch.
    /// The same ordering <see cref="StatementBinder"/> uses for a branch: the scope exists before the
    /// pattern binds into it.
    /// </summary>
    private static SwitchArm? BindArm(
        SwitchArmNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        var enclosing = ctx.Scope;
        ctx.Scope = new BodyScope(enclosing);
        var arm = BindArmParts(node, scrutineeType, ctx, context, scrutineeFailed);
        ctx.Scope = enclosing;

        return arm;
    }

    private static SwitchArm? BindArmParts(
        SwitchArmNode node, IMistType scrutineeType, BindingContext ctx, BuilderContext context, bool scrutineeFailed)
    {
        if(PatternBinder.Bind(node.Pattern, scrutineeType, ctx, context, scrutineeFailed) is not { } pattern)
        {
            return null;
        }

        Expression? guard = null;
        if(node.Guard is { } when)
        {
            using(ctx.PushTargetType(null))
            {
                guard = StatementBinder.AsCondition(OperatorBinder.BindVoidCoalescing(when.Condition, ctx, context), context);
            }
        }

        return new SwitchArm(node, pattern, guard, ExpressionBinder.BindExpression(node.Result, ctx, context));
    }

    private static IMistType? CommonArmType(
        IReadOnlyList<SwitchArm> arms, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        var values = arms.Select(arm => arm.Result).ToList();
        var common = CommonTypeInference.Infer(values, context.Resolver.LookupWellKnown, context.ProgramModel.TypeInterner);
        if(common is null)
        {
            var (first, other) = CommonTypeInference.Disagreement(values);
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.SwitchArmsNoCommonType, node, null, first.Name, other.Name);
        }

        return common;
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
        BuilderContext context,
        bool isImplicit = false)
    {
        var group = receiver as MethodGroupExpression ?? Callability.ExtensionMethods(receiver, ctx, context);

        if(group is null)
        {
            return BindFunctionInvocation(node, receiver, arguments, ctx, context);
        }

        if(group.Candidates.Count > 0 && group.Candidates.All(LocalFunctionBinder.IsRejected))
        {
            var retained = ArgumentBinder.Bind(arguments, group.Candidates, ctx, context, group.ReceiverConstructed, group.Paths);
            return new ErrorExpression(node, [receiver, .. retained]);
        }

        var bound = ArgumentBinder.Bind(
            arguments, group.Candidates, ctx, context, group.ReceiverConstructed, group.Paths, typeArguments);
        var interner = context.ProgramModel.TypeInterner;

        var choice = OverloadResolution.Select(
            group.Candidates, bound, group.ReceiverConstructed, typeArguments, interner,
            ArgumentBinder.RefFlags(arguments), group.Paths, new LambdaTrials(ctx, context), new ContextualExpressionTrials(ctx, context));

        if(choice.Selected is not { } selected)
        {
            return OverloadFailure.Retain(
                choice, group.Candidates, bound,
                $"method '{group.MemberName}'", node, context.Diagnostics, [receiver, .. bound]);
        }

        bound = ArgumentBinder.Settle(bound, selected.ParametersList, choice.Substitution, ctx, context);

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

        if(CloneConversionBody.Convert(node, group, selected, ctx, context) is { } conversion)
        {
            return conversion;
        }

        // The receiver's type arguments are already spelled in the receiver expression, and
        // repeating them on the member would be C# for something else entirely.
        var methodTypeArguments = selected.TypeParametersList
            .ConvertAll(parameter => choice.Substitution.Apply(parameter, interner));

        context.ProgramModel.ConstructorConstraintClosures.RecordMethod(selected, methodTypeArguments, choice.Substitution, node, ctx.Scope);
        context.ProgramModel.VariadicUses.Record(selected, methodTypeArguments);
        context.ProgramModel.GenericCalls.Record(selected, methodTypeArguments);

        if(TaskResult.IsDrop(selected))
        {
            context.ProgramModel.TaskDrops.RecordDrop(ctx.EnclosingMethod);
        }

        var callee = group.Receiver is null
            ? new MethodReferenceExpression(node, selected, methodTypeArguments)
            : (Expression)new MemberAccessExpression(
                node, group.Receiver, group.MemberName, returnType, methodTypeArguments, group.IsConditional);

        context.ProgramModel.TaskDrops.RecordCall(ctx.EnclosingMethod, selected);

        var parameterTypes = selected.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner));
        var invocation = new InvocationExpression(
            node, callee,
            OwnedProvenance.AtParameters(
                ImplicitConversions.AtParameters(bound, selected, parameterTypes, interner), selected.ParametersList),
            returnType, selected, parameterTypes)
        {
            IsImplicit = isImplicit,
            ContextTypes = ContextBinding.TypesOf(selected, type => choice.Substitution.Apply(type, interner)),
        };

        return TaskResult.IsDrop(selected) ? invocation : AutoDrop(invocation, ctx, context);
    }

    /// <summary>
    /// A task-returning call in a SYNC context yields its result, with the drop supplied here. The
    /// same call in an async context yields the task and the author drops it, which is the whole of
    /// the difference between the two contexts.
    /// <para>
    /// Keyed on the call's TYPE rather than on the callee being declared async, so a method returning
    /// a task it built itself reads the same as one the transform wrapped -- there is no way to tell
    /// them apart at the call site, and no reason to.
    /// </para>
    /// </summary>
    private static Expression AutoDrop(
        InvocationExpression invocation, BindingContext ctx, BuilderContext context)
    {
        // A POSITION WANTING THE TASK suppresses the insertion as surely as an async context does:
        // `Task<int> t = f();` asks for what f returns, and unwrapping it there would answer with a
        // type the position rejected.
        if(ctx.IsAsyncContext
            || (ctx.TargetType is { } target && TaskResult.IsTask(target))
            || !TaskResult.IsTask(invocation.Type))
        {
            return invocation;
        }

        return BindDrop(invocation.Node, invocation, isImplicit: true, ctx, context);
    }

    /// <summary>
    /// The Drop of a task, which <c>await</c> is sugar for (drop-semantics Req 7.5) and which a sync
    /// context inserts on a task-returning call. Bound as the member call it is, so every rule that
    /// reads a Drop -- the substructural counts, the scope-exit accounting -- sees one node for the
    /// three spellings, and a backend alone decides how its target completes a task. A drop unwraps
    /// exactly one level and is never itself auto-dropped: `await t` on a task of a task yields the
    /// inner task, whichever spelling asked.
    /// </summary>
    internal static Expression BindDrop(
        Storm.Runtime.SyntaxNode node, Expression task, bool isImplicit, BindingContext ctx, BuilderContext context)
    {
        var (found, constructed, _) = MemberResolution.Resolve(
            task.Type, "Drop", MemberVisibility.InstanceOnly, node, context, ctx.Scope);

        if(found is not MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new ErrorExpression(node);
        }

        var callee = new MethodGroupExpression(node, task, "Drop", group.Methods, constructed) { Paths = group.Paths };

        return BindInvocation(node, callee, arguments: null, typeArguments: null, ctx, context, isImplicit);
    }

    /// <summary>
    /// Invoking a VALUE rather than a method group, which resolves no member and chooses no overload:
    /// the receiver's function shape states what the call takes and produces, including the closed
    /// Invoke signature of a nominal CLR delegate. The receiver retains its declared type for emission.
    /// </summary>
    private static Expression BindFunctionInvocation(
        Storm.Runtime.SyntaxNode node,
        Expression receiver,
        ArgumentListNode? arguments,
        BindingContext ctx,
        BuilderContext context)
    {
        if(DelegateShape.Of(receiver.Type) is not { } function)
        {
            var untargeted = ArgumentBinder.Bind(arguments, ctx, context);

            return Callability.Refuses(receiver, node, context)
                ? new ErrorExpression(node, [receiver, .. untargeted])
                : new InvocationExpression(node, receiver, untargeted, receiver.Type, null, parameterTypes: null);
        }

        var bound = ArgumentBinder.BindTargeted(arguments, function.ParameterTypes, ctx, context);

        if(bound.Count != function.ParameterTypes.Count)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.FunctionCallArityMismatch, node, null,
                function.Name, function.ParameterTypes.Count, bound.Count);

            return new ErrorExpression(node, [receiver, .. bound]);
        }

        // Invoking a func VALUE takes the same auto-drop as invoking a method: the rule is about what
        // the call produces and where it sits, and a delegate returning a task is a task-returning
        // call however it was reached.
        var converted = ImplicitConversions.AtParameters(bound, method: null, function.ParameterTypes, context.ProgramModel.TypeInterner);
        var invoke = DelegateShape.InvokeOf(receiver.Type);
        var returnType = ClrInboundLifting.Close(
            invoke?.Modifiers ?? default, function.ReturnType,
            context.Resolver.LookupWellKnown(WellKnownTypes.Void), context.ProgramModel.TypeInterner);

        return AutoDrop(
            new InvocationExpression(node, receiver, converted, returnType, null, function.ParameterTypes)
            {
                InvokeMethod = invoke,
            }, ctx, context);
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
        if(!OptionalUnion.AdmitsConditionalAccess(receiver.Type))
        {
            return new MemberAccessExpression(node, receiver, memberName, ErrorType.Instance, isConditional: true);
        }

        var interner = context.ProgramModel.TypeInterner;
        var projected = OptionalLifting.NonVoidPortion(receiver.Type, interner);

        var (found, constructed, reported) = MemberResolution.Resolve(
            projected, memberName, MemberVisibility.InstanceOnly, node, context, ctx.Scope);

        if(found is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(
                node, receiver, memberName, group.Methods, constructed, isConditional: true)
            {
                Paths = group.Paths,
            };
        }

        var resolved = TypeSubstitution
            .ForMember(constructed, found, interner)
            .Apply(MemberTypeOf(found), interner);

        var lifted = OptionalLifting.Lift(
            resolved, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner);

        return new MemberAccessExpression(
            node, receiver, memberName, lifted, isConditional: true, member: found, isFailed: reported);
    }

    private static Expression BindMemberAccess(Storm.Runtime.SyntaxNode node, Expression receiver, string memberName, BindingContext ctx, BuilderContext context)
    {
        // A namespace is not a value, so `Simpliest.Program` is one qualified name
        // rather than a member access on a receiver. Resolve the qualification instead
        // of folding it, which also keeps the namespace sentinel from surviving into
        // the finished tree.
        if(receiver is NamespaceReferenceExpression namespaceRef)
        {
            return BindQualifiedName(node, namespaceRef, memberName, ctx, context);
        }

        var (found, constructed, reported) = ResolveMember(receiver, memberName, node, ctx, context);

        if(found is MethodGroupResult { Methods.Count: > 0 } group)
        {
            return new MethodGroupExpression(node, receiver, memberName, group.Methods, constructed) { Paths = group.Paths };
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
        // Through an instantiation, `Box<int>.Wrapper`, the nested type is carried under it, which the
        // substitution does for a definition exactly as it does for a member's type.
        if(found is TypeResult { Type: DeclaredType nested } typeResult)
        {
            context.ProgramModel.TypeNamings.Record(nested, ctx.Scope, node);
            var carried = receiver is TypeReferenceExpression { ReferencedType: ConstructedType instantiation }
                ? TypeSubstitution
                    .ForPath(instantiation, typeResult.BaseConstructions, context.ProgramModel.TypeInterner)
                    .Apply(nested, context.ProgramModel.TypeInterner)
                : nested;

            return new TypeReferenceExpression(node, carried);
        }

        var resolved = ClrInboundLifting.Close(
            found,
            TypeSubstitution
                .ForMember(constructed, found, context.ProgramModel.TypeInterner)
                .Apply(MemberTypeOf(found), context.ProgramModel.TypeInterner),
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            context.ProgramModel.TypeInterner);

        return new MemberAccessExpression(node, receiver, memberName, resolved, member: found, isFailed: reported);
    }

    /// <summary>
    /// Resolves one qualification step off a namespace receiver. A namespace can only
    /// contain types and further namespaces, so the result collapses the receiver
    /// rather than nesting onto it.
    /// </summary>
    private static Expression BindQualifiedName(
        Storm.Runtime.SyntaxNode node, NamespaceReferenceExpression namespaceRef, string memberName, BindingContext ctx, BuilderContext context)
    {
        var result = context.Resolver.LookupQualified(namespaceRef.NamespaceName, memberName);

        if(result is TypeResult named)
        {
            context.ProgramModel.TypeNamings.Record(named.Type, ctx.Scope, node);
        }

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
    private static MemberResolution.MemberLookup ResolveMember(
        Expression receiver, string memberName, Storm.Runtime.SyntaxNode node, BindingContext ctx, BuilderContext context)
    {
        // A dependent parameter named here is a value of its domain, a pack's being Type[], which makes
        // `TArgs.Length` the ordinary array length and `N.ToString()` its domain's member.
        if(receiver is TypeReferenceExpression { ReferencedType: TypeParameter { IsDependent: true, Domain: { } domain } })
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
}