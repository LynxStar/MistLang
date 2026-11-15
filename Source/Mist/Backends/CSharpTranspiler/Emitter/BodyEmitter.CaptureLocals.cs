using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
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
/// emitted before its readers. A struct closing nested in another type's argument is one such reader, its factory
/// built where the outer type is, and an element type's capture is kept where the length runs code, so the length
/// is read after it as written. An expression lambda declares the locals its own captures are kept in inside its
/// body, and a method group's delegate reads its value through a binding made each time the reference is
/// evaluated, so a call made later never reads another evaluation's value. A capture nothing consumes is a discarded read where its initializer runs code,
/// and what C# evaluates before the type is bound ahead of it so source order survives. A value-type receiver,
/// an assignment's target and an argument passed by reference are locations, which a binding would copy: what
/// is bound is what locates them, as <see cref="UnitHoisting"/> stores them, the owner stays where it is
/// written, and the expression or statement holding it evaluates its capture. A call returning by reference
/// cannot be bound either way, so it runs in place and a capture written after it rides the zero offset of
/// <c>Unsafe.Add</c> over what it returned. A call yielding nothing has no value to wrap, so its capture is a
/// statement ahead of it. A test evaluates its captures between the scrutinee and the type test so a failed
/// test does not skip them.
/// </summary>
internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static Dictionary<SlotCapture, string>? kept;

    [ThreadStatic]
    private static Dictionary<SlotCapture, string>? evaluated;

    [ThreadStatic]
    private static HashSet<SlotCapture>? settled;

    [ThreadStatic]
    private static int captureLocals;

    [ThreadStatic]
    private static HashSet<Expression>? sequenced;

    [ThreadStatic]
    private static Dictionary<Expression, List<SlotCapture>>? trailing;

    internal static IDisposable? EnterCaptureLocals(Block body)
    {
        if(kept is not null)
        {
            return null;
        }

        kept = new Dictionary<SlotCapture, string>(ReferenceEqualityComparer.Instance);
        settled = new HashSet<SlotCapture>(ReferenceEqualityComparer.Instance);

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
            settled = null;
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

                foreach(var captured in WitnessedCallSlots(arguments, parameters).SelectMany(witnessed => WitnessCaptures(witnessed.Argument)))
                {
                    if(!invocation.Captures.Contains(captured.Capture) && CarryingArgument(invocation, captured) is null)
                    {
                        yield return captured.Capture;
                    }
                }

                var witnessedCaptures = WitnessedCallSlots(arguments, parameters).SelectMany(witnessed => WitnessCaptures(witnessed.Argument)).ToList();

                foreach(var captured in FactoredCallCaptures(arguments, parameters).Where(captured => witnessedCaptures.Exists(read => ReferenceEquals(read.Capture, captured.Capture))))
                {
                    yield return captured.Capture;
                }

                foreach(var capture in invocation.Receiver is MemberAccessExpression { Receiver: TypeReferenceExpression staticOwner } ? ReadTwice(staticOwner.ReferencedType) : [])
                {
                    yield return capture;
                }

                break;

            case MemberAccessExpression { Receiver: TypeReferenceExpression heldThrough } held when HeldOwner(held) is { } holderOwner:
                foreach(var capture in ReadTwice(WrittenClosing(holderOwner, heldThrough)))
                {
                    yield return capture;
                }

                break;

            case ImplicitConversionExpression conversion:
                foreach(var capture in ConvertedCaptures(conversion))
                {
                    yield return capture;
                }

                break;

            case DefaultExpression defaulted:
                foreach(var capture in ClosingCaptures(defaulted.Type).Where(capture => !defaulted.Captures.Contains(capture)).Concat(NestedCaptures(defaulted.Type)))
                {
                    yield return capture;
                }

                break;

            case NewExpression creation:
                foreach(var capture in ClosingCaptures(creation.Type).Where(capture => !creation.Captures.Contains(capture)).Concat(NestedCaptures(creation.Type)).Concat(ReadTwice(creation.Type)))
                {
                    yield return capture;
                }

                break;

            case ArrayCreationExpression array:
                foreach(var capture in StampCaptures(array.ElementType, array.Size, array.Captures))
                {
                    yield return capture;
                }

                break;

            case StackallocExpression stack:
                foreach(var capture in StampCaptures(stack.ElementType, stack.Size, stack.Captures))
                {
                    yield return capture;
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

                foreach(var captured in WitnessedCallSlots(typeArguments, typeParameters).SelectMany(witnessed => WitnessCaptures(witnessed.Argument)))
                {
                    yield return captured.Capture;
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

        if(KeptLocal(capture) is not { } local)
        {
            return RenderAt(capture.Initializer, precedence, diagnostics);
        }

        return Settle(capture) ? $"({local} = {RenderAt(capture.Initializer, AssignmentPrecedence, diagnostics)})" : local;
    }

    private static bool Settle(SlotCapture capture)
    {
        return settled?.Add(capture) != false;
    }

    internal static void EvaluateAhead(IReadOnlyList<SlotCapture> captures, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var capture in captures.Where(Evaluates))
        {
            writer.WriteLine($"{KeptLocal(capture) ?? "_"} = {Render(capture.Initializer, diagnostics)};");
            _ = Settle(capture);
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

    private static void DeclareKeptLocals(object root, CSharpWriter writer)
    {
        if(kept is not { Count: > 0 })
        {
            return;
        }

        foreach(var owner in OwnersWithin(root))
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

    private static IEnumerable<object> OwnersWithin(object root)
    {
        var pending = new Stack<object>();
        pending.Push(root);

        while(pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;

            foreach(var child in BodyWalker.ChildrenOf(node).Reverse())
            {
                if(child is not (Statement or Block or LambdaExpression))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static bool KeepsWithin(Expression result)
    {
        return kept is { Count: > 0 } && OwnersWithin(result).SelectMany(SlotCapture.Of).Any(capture => KeptLocal(capture) is not null);
    }

    internal static bool OwnsCaptures(Statement statement)
    {
        return OwnersWithin(statement).SelectMany(SlotCapture.Of).Any(Evaluates);
    }

    private static string Keyword(SlotCapture capture)
    {
        return DependentCarrierNames.Keyword(capture.Value.Domain) ?? TypeNameEmitter.Emit(capture.Value.Domain);
    }

    private static bool EmitEvaluatingCaptures(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(trailing is not null && trailing.Remove(expression, out var after))
        {
            writer.Append($"System.Runtime.CompilerServices.Unsafe.Add(ref {Render(expression, diagnostics)}, {WrapDiscarded(after, "0", diagnostics)})");
            return true;
        }

        if(sequenced?.Contains(expression) == true
            || UnitHoisting.Substitution(expression) is not null
            || expression is not (InvocationExpression or NewExpression or DefaultExpression or ArrayCreationExpression or StackallocExpression
                or MemberAccessExpression or AssignmentExpression or CompoundAssignmentExpression))
        {
            return false;
        }

        var sequence = Leading(expression);

        if(sequence.IsEmpty)
        {
            return false;
        }

        writer.Append(Evaluating(sequence, () => Render(expression, diagnostics), diagnostics));
        return true;
    }

    private static string Evaluating(
        IReadOnlyList<SlotCapture> captures, Expression owner, Expression first, Func<string> inner, DiagnosticBag diagnostics, bool fresh = false)
    {
        if(captures.Count == 0)
        {
            return inner();
        }

        var sequence = new Sequence { Fresh = fresh };
        sequence.Steps.Add(new Ahead(first, null));
        sequence.Steps.AddRange(captures.Select(capture => new Ahead(null, capture)));
        sequence.Owners.Add(owner);

        return Evaluating(sequence, inner, diagnostics);
    }

    private static string Evaluating(Sequence sequence, Func<string> inner, DiagnosticBag diagnostics)
    {
        var enclosing = boundParts;
        var opened = new System.Text.StringBuilder();
        var depth = 0;
        var bound = new List<SlotCapture>();

        try
        {
            foreach(var step in sequence.Steps)
            {
                if(step.Capture is { } capture && sequence.Fresh && KeptLocal(capture) is not null && evaluated?.ContainsKey(capture) != true)
                {
                    var name = TemporaryName();
                    _ = opened.Append($"({Evaluation(capture, SwitchPrecedence + 1, diagnostics)} switch {{ var {name} => ");
                    (evaluated ??= new Dictionary<SlotCapture, string>(ReferenceEqualityComparer.Instance))[capture] = name;
                    bound.Add(capture);
                    depth++;
                }
                else if(step.Capture is { } discarded)
                {
                    _ = opened.Append($"({Evaluation(discarded, SwitchPrecedence + 1, diagnostics)} switch {{ _ => ");
                    depth++;
                }
                else if(step.Part is { } part && Binds(part))
                {
                    var name = TemporaryName();
                    _ = opened.Append($"({RenderAt(part, SwitchPrecedence + 1, diagnostics)} switch {{ var {name} => ");
                    boundParts = new Dictionary<Expression, string>(boundParts ?? [], ReferenceEqualityComparer.Instance) { [part] = name };
                    depth++;
                }
            }

            using var owners = new Sequenced(sequence);

            return $"{opened}{inner()}{string.Concat(Enumerable.Repeat(" })", depth))}";
        }
        finally
        {
            boundParts = enclosing;
            bound.ForEach(entry => evaluated?.Remove(entry));
        }
    }

    private static IDisposable? AtStatement(Expression? top, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(top is ImplicitConversionExpression conversion)
        {
            top = conversion.Operand;
        }

        if(top is null || sequenced?.Contains(top) == true || Leading(top) is not { IsEmpty: false } sequence)
        {
            return null;
        }

        var enclosing = boundParts;

        foreach(var step in sequence.Steps)
        {
            if(step.Capture is { } capture)
            {
                writer.WriteLine($"{KeptLocal(capture) ?? "_"} = {Render(capture.Initializer, diagnostics)};");
                _ = Settle(capture);
            }
            else if(step.Part is { } part && Binds(part))
            {
                var name = TemporaryName();
                writer.WriteLine($"{TypeNameEmitter.EmitInferable(part.Type)} {name} = {Render(part, diagnostics)};");
                boundParts = new Dictionary<Expression, string>(boundParts ?? [], ReferenceEqualityComparer.Instance) { [part] = name };
            }
        }

        return new StatementEvaluation(new Sequenced(sequence), enclosing);
    }

    private sealed class StatementEvaluation(Sequenced owners, Dictionary<Expression, string>? parts) : IDisposable
    {
        public void Dispose()
        {
            owners.Dispose();
            boundParts = parts;
        }
    }

    private sealed class Sequenced : IDisposable
    {
        private readonly List<Expression> entered = [];
        private readonly Sequence sequence;

        public Sequenced(Sequence sequence)
        {
            this.sequence = sequence;
            sequenced ??= new HashSet<Expression>(ReferenceEqualityComparer.Instance);
            trailing ??= new Dictionary<Expression, List<SlotCapture>>(ReferenceEqualityComparer.Instance);

            foreach(var owner in sequence.Owners)
            {
                if(sequenced.Add(owner))
                {
                    entered.Add(owner);
                }
            }

            foreach(var (returned, captures) in sequence.Trailing)
            {
                trailing[returned] = captures;
            }
        }

        public void Dispose()
        {
            foreach(var owner in entered)
            {
                _ = sequenced?.Remove(owner);
            }

            foreach(var returned in sequence.Trailing.Keys)
            {
                _ = trailing?.Remove(returned);
            }
        }
    }

    internal static bool EvaluatesCaptures(Expression? expression)
    {
        return expression is not null && !Leading(expression is ImplicitConversionExpression conversion ? conversion.Operand : expression).IsEmpty;
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
                _ = Settle(capture);
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