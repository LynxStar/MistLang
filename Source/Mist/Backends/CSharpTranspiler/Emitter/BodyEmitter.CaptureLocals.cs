using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Every owner of a capture evaluates its initializer exactly once, where the type is written, whether a hidden
/// argument consumes it, a later node reads it, or nothing does. A capture that a later node reads and that no
/// receiver or argument carries is kept in a C# local, declared before the statement that owns it and assigned
/// where the type is written (runtime-dependent-values design, <em>Identity by value: the first delivery</em>);
/// which captures those are is decided over the whole body before any of it is written, because the owner is
/// emitted before its readers. A capture nothing consumes is a discarded read where its initializer runs code,
/// a receiver or an operand written before the type is bound ahead of it so source order survives, and a test
/// evaluates its captures between the scrutinee and the type test so a failed test does not skip them.
/// </summary>
internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static Dictionary<SlotCapture, string>? kept;

    [ThreadStatic]
    private static Dictionary<SlotCapture, string>? evaluated;

    [ThreadStatic]
    private static int captureLocals;

    internal static IDisposable? EnterCaptureLocals(Block body)
    {
        if(kept is not null)
        {
            return null;
        }

        kept = new Dictionary<SlotCapture, string>(ReferenceEqualityComparer.Instance);

        foreach(var node in BodyWalker.DescendantsAndSelf(body))
        {
            foreach(var capture in UncarriedReads(node))
            {
                if(!kept.ContainsKey(capture))
                {
                    kept[capture] = $"__c{++captureLocals}";
                }
            }
        }

        return new CaptureLocals();
    }

    private sealed class CaptureLocals : IDisposable
    {
        public void Dispose()
        {
            kept = null;
        }
    }

    private static IEnumerable<SlotCapture> UncarriedReads(object node)
    {
        switch(node)
        {
            case InvocationExpression invocation when CallTypeArguments(invocation) is var (arguments, parameters, _):
                foreach(var (slot, argument) in RoutedCallSlots(arguments, parameters))
                {
                    if(argument is CapturedValue captured
                        && !invocation.Captures.Contains(captured.Capture)
                        && ReceiverRead(invocation, slot) is null
                        && CarryingArgument(invocation, captured) is null)
                    {
                        yield return captured.Capture;
                    }
                }

                break;

            case FunctionReferenceExpression reference:
                var (typeArguments, typeParameters, method) = CallTypeArguments(reference.Method, reference.TypeArguments);

                foreach(var (slot, argument) in RoutedCallSlots(typeArguments, typeParameters))
                {
                    if(argument is CapturedValue captured
                        && (method.Modifiers.IsStatic || ReceiverMember(method, slot) is null)
                        && CarryingParameter(method, slot) is null)
                    {
                        yield return captured.Capture;
                    }
                }

                break;
        }
    }

    private static string? KeptLocal(SlotCapture capture)
    {
        return kept?.GetValueOrDefault(capture);
    }

    private static bool Evaluates(SlotCapture capture)
    {
        return KeptLocal(capture) is not null || !IsPlainRead(capture.Initializer);
    }

    private static string Evaluation(SlotCapture capture, int precedence, DiagnosticBag diagnostics)
    {
        if(evaluated?.GetValueOrDefault(capture) is { } bound)
        {
            return bound;
        }

        return KeptLocal(capture) is { } local
            ? $"({local} = {RenderAt(capture.Initializer, AssignmentPrecedence, diagnostics)})"
            : RenderAt(capture.Initializer, precedence, diagnostics);
    }

    private static void EvaluateDiscarded(IEnumerable<SlotCapture> captures, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var capture in captures.Where(Evaluates))
        {
            writer.WriteLine($"{KeptLocal(capture) ?? "_"} = {Render(capture.Initializer, diagnostics)};");
        }
    }

    private static string WrapDiscarded(IEnumerable<SlotCapture> captures, string inner, DiagnosticBag diagnostics)
    {
        foreach(var capture in captures.Where(Evaluates).Reverse())
        {
            inner = $"({Evaluation(capture, SwitchPrecedence + 1, diagnostics)} switch {{ _ => {inner} }})";
        }

        return inner;
    }

    private static void DeclareKeptLocals(Statement statement, CSharpWriter writer)
    {
        if(kept is not { Count: > 0 })
        {
            return;
        }

        foreach(var owner in OwnersWithin(statement))
        {
            foreach(var capture in SlotCapture.Of(owner))
            {
                if(KeptLocal(capture) is { } local)
                {
                    writer.WriteLine($"{Keyword(capture)} {local};");
                }
            }
        }
    }

    private static IEnumerable<object> OwnersWithin(Statement statement)
    {
        var pending = new Stack<object>();
        pending.Push(statement);

        while(pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;

            foreach(var child in BodyWalker.ChildrenOf(node).Reverse())
            {
                if(child is not (Statement or Block))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string Keyword(SlotCapture capture)
    {
        return DependentCarrierNames.Keyword(capture.Value.Domain) ?? TypeNameEmitter.Emit(capture.Value.Domain);
    }

    private static IEnumerable<SlotCapture> Unconsumed(Expression expression)
    {
        return expression switch
        {
            NewExpression creation => creation.Captures.Where(capture => !ConsumedByClosing(creation.Type, capture)),
            InvocationExpression invocation => invocation.Captures.Where(capture => !ConsumedByCall(invocation, capture)),
            MemberAccessExpression access => access.Captures.Where(capture => !ConsumedByAccess(access, capture)),
            _ => [],
        };
    }

    private static bool ConsumesCapture(Expression? initializer, SlotCapture capture)
    {
        var inner = initializer is ImplicitConversionExpression conversion ? conversion.Operand : initializer;

        return inner is DefaultExpression defaulted && ConsumedByClosing(defaulted.Type, capture);
    }

    private static bool ConsumedByClosing(IMistType type, SlotCapture capture)
    {
        return Unaliased(TypeNameEmitter.Closed(type)) is ConstructedType constructed
            && SlotRoutes.ValueRouted(constructed.Definition).Any(slot =>
                slot.Ordinal < constructed.TypeArguments.Count
                && constructed.TypeArguments[slot.Ordinal] is CapturedValue captured
                && ReferenceEquals(captured.Capture, capture));
    }

    private static bool ConsumedByCall(InvocationExpression invocation, SlotCapture capture)
    {
        if(CallTypeArguments(invocation) is not var (arguments, parameters, method))
        {
            return false;
        }

        if(RoutedCallSlots(arguments, parameters).Any(routed => routed.Argument is CapturedValue captured && ReferenceEquals(captured.Capture, capture)))
        {
            return true;
        }

        return method is { Modifiers.IsStatic: true, DeclaringType: MemberType }
            && invocation.Receiver is MemberAccessExpression { Receiver: TypeReferenceExpression reference }
            && ConsumedByClosing(reference.ReferencedType, capture);
    }

    private static bool ConsumedByAccess(MemberAccessExpression access, SlotCapture capture)
    {
        return ProjectedValueAccess.Declaring(access) is { } declaring
            && access.Member is PropertyResult { Property.ProjectedParameter: { } parameter }
            && SlotRoutes.IsValueRouted(parameter)
            && Unaliased(TypeNameEmitter.Closed(declaring)) is ConstructedType closing
            && parameter.Ordinal < closing.TypeArguments.Count
            && closing.TypeArguments[parameter.Ordinal] is CapturedValue captured
            && ReferenceEquals(captured.Capture, capture);
    }

    private static Expression? FirstEvaluated(Expression expression)
    {
        return expression switch
        {
            InvocationExpression { Receiver: MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression } } => receiver,
            MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression } => receiver,
            CastExpression cast => cast.Operand,
            AsExpression conversion => conversion.Operand,
            _ => null,
        };
    }

    private static bool EmitEvaluatingCaptures(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(ReferenceEquals(discarding, expression))
        {
            return false;
        }

        var pending = Unconsumed(expression).Where(Evaluates).ToList();

        if(pending.Count == 0)
        {
            return false;
        }

        var enclosing = discarding;
        discarding = expression;

        try
        {
            writer.Append(Evaluating(pending, FirstEvaluated(expression), () => Render(expression, diagnostics), diagnostics));
        }
        finally
        {
            discarding = enclosing;
        }

        return true;
    }

    private static string Evaluating(IReadOnlyList<SlotCapture> captures, Expression? first, Func<string> inner, DiagnosticBag diagnostics)
    {
        if(captures.Count == 0)
        {
            return inner();
        }

        if(first is null || IsPlainRead(first))
        {
            return WrapDiscarded(captures, inner(), diagnostics);
        }

        var name = TemporaryName();
        var rendered = RenderAt(first, SwitchPrecedence + 1, diagnostics);
        var enclosing = boundParts;
        boundParts = new Dictionary<Expression, string>(enclosing ?? [], ReferenceEqualityComparer.Instance) { [first] = name };

        try
        {
            return $"({rendered} switch {{ var {name} => {WrapDiscarded(captures, inner(), diagnostics)} }})";
        }
        finally
        {
            boundParts = enclosing;
        }
    }

    private static IDisposable? AtStatement(Expression? top, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(top is ImplicitConversionExpression conversion)
        {
            top = conversion.Operand;
        }

        if(top is null)
        {
            return null;
        }

        var pending = Unconsumed(top).Where(Evaluates).ToList();

        if(pending.Count == 0)
        {
            return null;
        }

        var first = FirstEvaluated(top);
        var enclosingParts = boundParts;

        if(first is not null && !IsPlainRead(first))
        {
            var name = TemporaryName();
            writer.WriteLine($"{TypeNameEmitter.EmitInferable(first.Type)} {name} = {Render(first, diagnostics)};");
            boundParts = new Dictionary<Expression, string>(enclosingParts ?? [], ReferenceEqualityComparer.Instance) { [first] = name };
        }

        EvaluateDiscarded(pending, writer, diagnostics);
        var enclosing = discarding;
        discarding = top;

        return new StatementEvaluation(enclosing, enclosingParts);
    }

    private sealed class StatementEvaluation(Expression? enclosing, Dictionary<Expression, string>? parts) : IDisposable
    {
        public void Dispose()
        {
            discarding = enclosing;
            boundParts = parts;
        }
    }

    internal static bool EvaluatesCaptures(Expression? expression)
    {
        return expression is not null && Unconsumed(expression is ImplicitConversionExpression conversion ? conversion.Operand : expression).Any(Evaluates);
    }

    private static List<SlotCapture> Evaluated(Pattern pattern)
    {
        return [.. BodyWalker.DescendantsAndSelf(pattern).OfType<TypeTestPattern>().SelectMany(test => test.Captures).Where(Evaluates)];
    }

    private static IDisposable EmitPrelude(IReadOnlyList<SlotCapture> captures, string lead, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        evaluated ??= new Dictionary<SlotCapture, string>(ReferenceEqualityComparer.Instance);

        foreach(var capture in captures)
        {
            if(KeptLocal(capture) is { } local)
            {
                writer.Append($"{lead}({local} = {RenderAt(capture.Initializer, AssignmentPrecedence, diagnostics)}) is var _");
                evaluated[capture] = local;
            }
            else
            {
                var name = TemporaryName();
                writer.Append($"{lead}{RenderAt(capture.Initializer, RelationalPrecedence, diagnostics)} is var {name}");
                evaluated[capture] = name;
            }

            lead = " && ";
        }

        return new Prelude(captures);
    }

    private sealed class Prelude(IReadOnlyList<SlotCapture> captures) : IDisposable
    {
        public void Dispose()
        {
            foreach(var capture in captures)
            {
                _ = evaluated?.Remove(capture);
            }
        }
    }
}