using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Walks the precedence chain from VoidCoalescing down to Unary.
/// Each level binds its operator into a <see cref="BinaryExpression"/> when present,
/// or passes through to the next level when bare.
/// </summary>
internal static class OperatorBinder
{
    internal static Expression BindVoidCoalescing(VoidCoalescingNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindLogicalOr(node.Left, ctx, context);

        if(IsBare(node.LogicalOrs, node.Left, node.Right))
        {
            return left;
        }

        // Each link un-lifts: the right operand binds against the non-void portion of its left, since it
        // supplies a value exactly when the left holds absence -- and the link IS that portion unless the
        // right operand is itself still optional, in which case the absence survives to the next link.
        foreach(var operand in node.LogicalOrs)
        {
            var interner = context.ProgramModel.TypeInterner;
            var portion = OptionalLifting.NonVoidPortion(left.Type, interner);

            Expression right;
            using(ctx.PushTargetType(portion))
            {
                right = BindLogicalOr(operand, ctx, context);
            }

            var resultType = OptionalUnion.ContainsVoid(right.Type)
                ? OptionalLifting.Lift(portion, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner)
                : portion;

            left = new CoalesceExpression(node, left, right, resultType);
        }

        return left;
    }

    private static Expression BindLogicalOr(LogicalOrNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindLogicalAnd(node.Left, ctx, context);

        if(IsBare(node.LogicalAnds, node.Left, node.Right))
        {
            return left;
        }

        // Left-associative: fold repeated operands.
        foreach(var operand in node.LogicalAnds)
        {
            var right = BindLogicalAnd(operand, ctx, context);
            left = MakeBinary(node, left, BinaryOperatorKind.LogicalOr, right, context);
        }

        return left;
    }

    private static Expression BindLogicalAnd(LogicalAndNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindEquality(node.Left, ctx, context);

        if(IsBare(node.Equalitys, node.Left, node.Right))
        {
            return left;
        }

        foreach(var operand in node.Equalitys)
        {
            var right = BindEquality(operand, ctx, context);
            left = MakeBinary(node, left, BinaryOperatorKind.LogicalAnd, right, context);
        }

        return left;
    }

    private static Expression BindEquality(EqualityNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindRelational(node.Left, ctx, context);

        // Left-associative: fold each (operator, operand) pair, retaining all operands.
        foreach(var group in node.Group0)
        {
            var op = MapOperator(group.Operator.Lexeme);
            var right = BindRelational(group.Right, ctx, context);
            left = MakeBinary(node, left, op, right, context);
        }

        return left;
    }

    private static Expression BindRelational(RelationalNode node, BindingContext ctx, BuilderContext context)
    {
        switch(node)
        {
            case IsExpressionRelationalNode isExpression:
                return BindIs(isExpression, ctx, context);

            case AsExpressionRelationalNode asExpression:
                return BindAs(asExpression, ctx, context);

            case RelationalComparisonRelationalNode comparison:
                {
                    var left = BindAdditive(comparison.Left, ctx, context);

                    foreach(var group in comparison.Group0)
                    {
                        var op = MapOperator(group.Operator.Lexeme);
                        var right = BindAdditive(group.Right, ctx, context);
                        left = MakeBinary(node, left, op, right, context);
                    }

                    return left;
                }

            default:
                return new UnresolvedExpression(node);
        }
    }

    private static Expression BindIs(
        IsExpressionRelationalNode node, BindingContext ctx, BuilderContext context)
    {
        // The target type is pushed away for the same reason the cast arm pushes it away: a test asks
        // what the scrutinee already is, so the boolean this position expects is the one type it is not.
        Expression scrutinee;
        using(ctx.PushTargetType(null))
        {
            scrutinee = BindAdditive(node.Left, ctx, context);
        }

        var pattern = PatternBinder.Bind(node.Pattern, scrutinee.Type, ctx, context);

        if(pattern is null)
        {
            return new UnresolvedExpression(node);
        }

        if(pattern.NarrowedType is ErrorType)
        {
            return new ErrorExpression(node);
        }

        return new IsPatternExpression(
            node, scrutinee, pattern, context.Resolver.LookupWellKnown(WellKnownTypes.Boolean));
    }

    private static Expression BindAs(
        AsExpressionRelationalNode node, BindingContext ctx, BuilderContext context)
    {
        Expression operand;
        using(ctx.PushTargetType(null))
        {
            operand = BindAdditive(node.Left, ctx, context);
        }

        var target = context.Resolver.Resolve(node.TargetType, ctx.Scope);

        if(operand.Type is ErrorType || target is ErrorType)
        {
            return new ErrorExpression(node);
        }

        var (kind, constituent) = Classify(operand.Type, target);

        if(kind is not { } classified)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.AsConversionNoRelationship, node, null,
                operand.Type.Name, target.Name);

            return new ErrorExpression(node);
        }

        var resultType = classified is AsConversionKind.Identity or AsConversionKind.Widening
            ? target
            : OptionalLifting.Lift(
                target,
                context.Resolver.LookupWellKnown(WellKnownTypes.Void),
                context.ProgramModel.TypeInterner)
            ;

        return new AsExpression(node, operand, target, classified, constituent, resultType);
    }

    /// <summary>
    /// How the conversion reaches its target, or null where nothing relates the two. The ORDER is forced.
    /// Containment answers PROVABLE before anything answers checked, so a conversion that cannot fail is
    /// never classified as one that can; and the union arm precedes containment-in-reverse because a
    /// constituent has to be reached through its own carrier accessor, a carrier standing in no relation
    /// to what it holds.
    /// <para>
    /// <c>unknown</c> is an arm of its own because <see cref="ConversionRelation"/> declines to reason
    /// about it, answering false in both directions. Every value inhabits it, so narrowing out of it is
    /// checked rather than unrelated.
    /// </para>
    /// <para>
    /// TWO RELATIONSHIPS ARE DELIBERATELY REFUSED, and adding them means finding a spelling first rather
    /// than just widening this. Two siblings under one base, and a struct against an abstract it does not
    /// declare, are both reachable in principle and neither is expressible as C#'s <c>as</c>: it demands a
    /// reference conversion, which sibling classes do not have and a struct cannot acquire by subclassing.
    /// So admitting them here would report nothing and emit C# that Roslyn rejects.
    /// </para>
    /// </summary>
    private static (AsConversionKind? Kind, IMistType? Constituent) Classify(
        IMistType source, IMistType target)
    {
        if(ConversionRelation.IsIdentity(source, target))
        {
            return (AsConversionKind.Identity, null);
        }

        if(ConversionRelation.IsWithin(source, target))
        {
            return (AsConversionKind.Widening, null);
        }

        if(source is UnionType union
            && !OptionalUnion.IsOptional(union)
            && PatternBinder.MatchConstituent(union, target) is { } constituent)
        {
            return (AsConversionKind.UnionExtraction, constituent);
        }

        if(source is UnknownType || ConversionRelation.IsWithin(target, source))
        {
            return (AsConversionKind.Narrowing, null);
        }

        return (null, null);
    }

    private static Expression BindAdditive(AdditiveNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindMultiplicative(node.Left, ctx, context);

        foreach(var group in node.Group0)
        {
            var op = MapOperator(group.Operator.Lexeme);
            var right = BindMultiplicative(group.Right, ctx, context);
            left = MakeBinary(node, left, op, right, context);
        }

        return left;
    }

    private static Expression BindMultiplicative(MultiplicativeNode node, BindingContext ctx, BuilderContext context)
    {
        var left = BindRange(node.Left, ctx, context);

        foreach(var group in node.Group0)
        {
            var op = MapOperator(group.Operator.Lexeme);
            var right = BindRange(group.Right, ctx, context);
            left = MakeBinary(node, left, op, right, context);
        }

        return left;
    }

    private static Expression BindRange(RangeNode node, BindingContext ctx, BuilderContext context)
    {
        if(node is NonRangeRangeNode nonRange)
        {
            return BindUnary(nonRange.Operand, ctx, context);
        }

        if(node is not RangeExpressionRangeNode range)
        {
            return new UnresolvedExpression(node);
        }

        var start = range.Start is { } startNode ? BindUnary(startNode, ctx, context) : null;
        var end = range.End is { } endNode ? BindUnary(endNode, ctx, context) : null;

        return new RangeExpression(
            node, start, end, context.Resolver.LookupWellKnown(WellKnownTypes.Range));
    }

    private static Expression BindUnary(UnaryNode node, BindingContext ctx, BuilderContext context)
    {
        switch(node)
        {
            case PostfixExpressionUnaryNode postfixExpression:
                return PostfixBinder.BindPostfix(postfixExpression.Primary, ctx, context);

            case NegateUnaryNode negate:
                {
                    var operand = BindUnary(negate.Operand, ctx, context);
                    return new UnaryExpression(
                        node, UnaryOperatorKind.Negate, operand,
                        OperatorAdmissibility.UnaryResultType(UnaryOperatorKind.Negate, operand.Type))
                    {
                        Operator = OperatorAdmissibility.SelectedUnary(UnaryOperatorKind.Negate, operand.Type),
                    };
                }

            case LogicalNotUnaryNode logicalNot:
                {
                    var operand = BindUnary(logicalNot.Operand, ctx, context);
                    return new UnaryExpression(
                        node, UnaryOperatorKind.LogicalNot, operand,
                        OperatorAdmissibility.UnaryResultType(UnaryOperatorKind.LogicalNot, operand.Type))
                    {
                        Operator = OperatorAdmissibility.SelectedUnary(UnaryOperatorKind.LogicalNot, operand.Type),
                    };
                }

            // Alone among the unary arms this does not answer with its operand's type. `^1` is an
            // OFFSET FROM THE END and not the integer 1, so an element access has to be able to tell
            // the two apart to know whether it was handed a position or a count -- which is what makes
            // the offset a type here rather than a spelling the emitter recovers.
            case FromEndExpressionUnaryNode fromEnd:
                {
                    var operand = BindUnary(fromEnd.Operand, ctx, context);
                    return new UnaryExpression(
                        node,
                        UnaryOperatorKind.FromEnd,
                        operand,
                        context.Resolver.LookupWellKnown(WellKnownTypes.Index));
                }

            case AsyncBlockExpressionUnaryNode asyncBlock:
                return BindAsyncBlock(node, asyncBlock.Body, isValueTask: false, ctx, context);

            // The hint names the task kind. Anything but `valuetask` reads as the plain task rather
            // than being refused: which names are admitted is a rule about a finished model.
            case AsyncHintBlockExpressionUnaryNode hinted:
                return BindAsyncBlock(
                    node, hinted.Body,
                    string.Equals(hinted.Hint.Lexeme, "valuetask", StringComparison.Ordinal), ctx, context);

            case AsyncExpressionUnaryNode asyncExpression:
                {
                    var operand = Suppressed(asyncExpression.Operand, ctx, context);

                    return TaskResult.IsTask(operand.Type)
                        ? new AsyncExpression(node, operand, operand.Type, isLift: false)
                        : new AsyncExpression(
                            node, operand, context.Resolver.ConstructTask(operand.Type), isLift: true)
                        ;
                }

            // The operand is bound with no target: `await` says what to do with a task, and a position
            // expecting the RESULT would otherwise push that result down as the operand's target,
            // which is the one type the operand is not.
            case AwaitExpressionUnaryNode await:
                {
                    var operand = Suppressed(await.Operand, ctx, context);

                    if(!TaskResult.IsTask(operand.Type))
                    {
                        context.Diagnostics.ReportAtNode(
                            ProgramModelDiagnostics.AwaitOfNonTask, node, null, operand.Type.Name);

                        return new ErrorExpression(node);
                    }

                    return PostfixBinder.BindDrop(node, operand, isImplicit: false, ctx, context);
                }

            case CastExpressionUnaryNode cast:
                {
                    // The target type is pushed away rather than through: an operand that reads its
                    // position's expected type would take the cast's own target for it, which is the
                    // one type a cast says the operand is NOT already.
                    Expression operand;
                    using(ctx.PushTargetType(null))
                    {
                        operand = BindUnary(cast.Operand, ctx, context);
                    }

                    var targetType = context.Resolver.Resolve(cast.TargetType, ctx.Scope);
                    var called = CastOperatorSelection.Best(
                        operand.Type, targetType, context.ProgramModel.TypeInterner);

                    return new CastExpression(
                        node, operand, targetType, called,
                        called is [var selected] ? selected.TargetType : CastResultType(operand.Type, targetType, context));
                }

            default:
                return new UnresolvedExpression(node);
        }
    }

    private static IMistType CastResultType(IMistType operand, IMistType target, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;

        return target is EnumType
            && ConversionClassifier.Classify(operand, target, interner) is ConversionKind.Explicit or ConversionKind.None
            ? OptionalLifting.Lift(target, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner)
            : target
            ;
    }

    /// <summary>
    /// An operand of <c>await</c> or of the <c>async</c> functor: bound with no target, since both say
    /// what to do with a TASK and a position expecting the result would push that result down as the
    /// operand's own target, which is the one type it is not.
    /// <para>
    /// An auto-drop the sync context inserted is then UNDONE rather than stacked. Both operators ask
    /// for the task, so `await f()` drops what f produces rather than the value already unwrapped from
    /// it, and `async f()` yields that task rather than lifting the unwrapped value back into a new one.
    /// </para>
    /// </summary>
    /// <summary>
    /// The block is an ASYNC context of its own: it produces the task, so a call inside it yields a
    /// task rather than being awaited, exactly as inside an async method.
    /// </summary>
    private static Expression BindAsyncBlock(
        UnaryNode node, BlockNode bodyNode, bool isValueTask, BindingContext ctx, BuilderContext context)
    {
        var body = new Block(bodyNode);
        var enclosingIsAsync = ctx.IsAsyncContext;
        var enclosingReturn = ctx.ReturnType;
        ctx.IsAsyncContext = true;
        ctx.ReturnType = null;

        try
        {
            BodyBinder.BindNestedBlock(body, ctx.Scope, ctx, context, returnType: null);
        }
        finally
        {
            ctx.IsAsyncContext = enclosingIsAsync;
            ctx.ReturnType = enclosingReturn;
        }

        var produced = LambdaBinder.InferredReturnType(body, node, context)
            ?? context.Resolver.LookupWellKnown(WellKnownTypes.Void);

        var kind = isValueTask ? WellKnownTypes.ValueTaskGeneric : WellKnownTypes.TaskGeneric;

        return new AsyncBlockExpression(
            node, body, context.Resolver.ConstructTask(kind, produced), isValueTask);
    }

    private static Expression Suppressed(UnaryNode operandNode, BindingContext ctx, BuilderContext context)
    {
        Expression operand;
        using(ctx.PushTargetType(null))
        {
            operand = BindUnary(operandNode, ctx, context);
        }

        return operand is InvocationExpression { IsImplicit: true } inserted && TaskResult.Dropped(inserted) is { } task
            ? task
            : operand;
    }

    private static BinaryExpression MakeBinary(
        Storm.Runtime.SyntaxNode node, Expression left, BinaryOperatorKind op, Expression right, BuilderContext context)
    {
        return new BinaryExpression(node, left, op, right, BinaryResultType(left, op, right, context))
        {
            Operator = OperatorAdmissibility.Selected(op, left.Type, right.Type),
        };
    }

    /// <summary>
    /// The type <see cref="OperatorAdmissibility"/> gives the operator over these operands, or the error
    /// type where no operator admits them: the operator rule reports that against the operands, and an
    /// error-typed result keeps everything downstream from complaining about it a second time.
    /// </summary>
    private static IMistType BinaryResultType(Expression left, BinaryOperatorKind op, Expression right, BuilderContext context)
    {
        return OperatorAdmissibility.ResultType(op, left.Type, right.Type, context.Resolver.LookupWellKnown)
            ?? ErrorType.Instance;
    }

    private static BinaryOperatorKind MapOperator(string? lexeme)
    {
        return lexeme switch
        {
            "+" => BinaryOperatorKind.Add,
            "-" => BinaryOperatorKind.Subtract,
            "*" => BinaryOperatorKind.Multiply,
            "/" => BinaryOperatorKind.Divide,
            "%" => BinaryOperatorKind.Modulo,
            "==" => BinaryOperatorKind.Equal,
            "!=" => BinaryOperatorKind.NotEqual,
            "<" => BinaryOperatorKind.LessThan,
            ">" => BinaryOperatorKind.GreaterThan,
            "<=" => BinaryOperatorKind.LessThanOrEqual,
            ">=" => BinaryOperatorKind.GreaterThanOrEqual,
            "&&" => BinaryOperatorKind.LogicalAnd,
            "||" => BinaryOperatorKind.LogicalOr,
            _ => BinaryOperatorKind.Add, // fallback
        };
    }

    /// <summary>
    /// Determines whether a precedence-chain node has no operator applied. Shared with
    /// <see cref="PatternBinder"/>, whose or/and levels repeat their operands the same way.
    /// </summary>
    internal static bool IsBare<T>(ImmutableArray<T> repeatedOperands, T left, T? right)
        where T : class
    {
        return repeatedOperands.IsDefaultOrEmpty && (right is null || ReferenceEquals(right, left));
    }
}