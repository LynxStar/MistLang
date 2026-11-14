using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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

    internal static bool OwnsCaptures(Statement statement)
    {
        return OwnersWithin(statement).SelectMany(SlotCapture.Of).Any(Evaluates);
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

    private enum Reading
    {
        Value,
        Receiver,
        Reference,
        Target,
    }

    private readonly record struct Ahead(Expression? Part, SlotCapture? Capture, bool InPlace = false);

    private sealed class Sequence
    {
        public List<Ahead> Steps { get; } = [];

        public List<Expression> Owners { get; } = [];

        public Dictionary<Expression, List<SlotCapture>> Trailing { get; } = new(ReferenceEqualityComparer.Instance);

        public bool IsEmpty => Steps.Count == 0 && Trailing.Count == 0;
    }

    private static Sequence Leading(Expression expression)
    {
        var sequence = new Sequence();
        Lead(expression, sequence);
        var last = sequence.Steps.FindLastIndex(step => step.Capture is not null);
        sequence.Steps.RemoveRange(last + 1, sequence.Steps.Count - last - 1);
        sequence.Owners.Add(expression);
        Expression? returned = null;

        foreach(var step in sequence.Steps.ToList())
        {
            if(step.InPlace)
            {
                returned = step.Part;
                _ = sequence.Steps.Remove(step);
            }
            else if(step.Capture is { } capture && returned is not null)
            {
                _ = sequence.Steps.Remove(step);

                if(!sequence.Trailing.TryGetValue(returned, out var trailing))
                {
                    sequence.Trailing[returned] = trailing = [];
                }

                trailing.Add(capture);
            }
        }

        return sequence;
    }

    private static void Lead(Expression expression, Sequence sequence)
    {
        switch(expression)
        {
            case InvocationExpression invocation:
                if(invocation.Receiver is MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression })
                {
                    Place(receiver, Held(receiver), sequence);
                }

                Own(invocation, sequence);
                PlaceArguments(invocation.Arguments, invocation.Parameters, sequence);
                break;

            case NewExpression creation:
                Own(creation, sequence);
                PlaceArguments(creation.Arguments, creation.Constructor?.ParametersList, sequence);
                break;

            case MemberAccessExpression access:
                if(access.Receiver is not TypeReferenceExpression)
                {
                    Place(access.Receiver, Held(access.Receiver), sequence);
                }

                Own(access, sequence);
                break;

            case AssignmentExpression assignment:
                Place(assignment.Target, Reading.Target, sequence);
                break;

            case CompoundAssignmentExpression compound:
                Place(compound.Target, Reading.Target, sequence);
                break;
        }
    }

    private static void Own(Expression owner, Sequence sequence)
    {
        var pending = sequenced?.Contains(owner) == true ? [] : Unconsumed(owner).Where(Evaluates).ToList();

        if(pending.Count > 0)
        {
            sequence.Steps.AddRange(pending.Select(capture => new Ahead(null, capture)));
            sequence.Owners.Add(owner);
        }
    }

    private static void PlaceArguments(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, Sequence sequence)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            var byReference = parameters is not null && i < parameters.Count && ArgumentKeywords.For(parameters[i]).Length > 0;
            Place(arguments[i], byReference ? Reading.Reference : Reading.Value, sequence);
        }
    }

    private static Reading Held(Expression receiver)
    {
        var type = TypeNameEmitter.Closed(receiver.Type);

        return type is TypeParameter || TypeNameEmitter.IsValueKind(type) ? Reading.Receiver : Reading.Value;
    }

    private static void Place(Expression operand, Reading reading, Sequence sequence)
    {
        switch(operand)
        {
            case var _ when reading is Reading.Value:
                sequence.Steps.Add(new Ahead(operand, null));
                break;

            case InvocationExpression { Signature.Modifiers: { IsRef: true } returned }:
                Lead(operand, sequence);

                if(!returned.ReturnsReadonlyRef)
                {
                    sequence.Steps.Add(new Ahead(operand, null, InPlace: true));
                }

                break;

            case MemberAccessExpression { Receiver: TypeReferenceExpression }:
            case MemberAccessExpression { Member: FieldResult }:
            case MemberAccessExpression when reading is Reading.Target:
                Lead(operand, sequence);
                break;

            case IndexExpression index:
                Place(index.Receiver, Held(index.Receiver), sequence);

                if(index.Index is not LiteralExpression)
                {
                    Place(index.Index, Reading.Value, sequence);
                }

                break;

            case not FieldReferenceExpression when reading is Reading.Receiver:
                sequence.Steps.Add(new Ahead(operand, null));
                break;
        }
    }

    private static bool Binds(Expression part)
    {
        return !IsPlainRead(part)
            && !UnitHoisting.IsStable(part)
            && part is not (ArrayLiteralExpression or FunctionReferenceExpression)
            && boundParts?.ContainsKey(part) != true;
    }

    private static IEnumerable<Expression> BoundAhead(Expression operand, Reading reading)
    {
        var sequence = new Sequence();
        Place(operand, reading, sequence);

        return sequence.Steps.Where(step => !step.InPlace).Select(step => step.Part).OfType<Expression>().Where(part => !IsPlainRead(part) && boundParts?.ContainsKey(part) != true);
    }

    private static bool RunsInPlace(Expression receiver)
    {
        return Held(receiver) is Reading.Receiver && receiver switch
        {
            InvocationExpression { Signature.Modifiers.IsRef: true } => true,
            MemberAccessExpression { Member: FieldResult } access => RunsInPlace(access.Receiver),
            IndexExpression index => RunsInPlace(index.Receiver),
            _ => false,
        };
    }

    private static bool EmitEvaluatingCaptures(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(trailing is not null && trailing.Remove(expression, out var after))
        {
            writer.Append($"System.Runtime.CompilerServices.Unsafe.Add(ref {Render(expression, diagnostics)}, {WrapDiscarded(after, "0", diagnostics)})");
            return true;
        }

        if(sequenced?.Contains(expression) == true
            || expression is not (InvocationExpression or NewExpression or MemberAccessExpression or AssignmentExpression or CompoundAssignmentExpression))
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

    private static string Evaluating(IReadOnlyList<SlotCapture> captures, Expression owner, Expression first, Func<string> inner, DiagnosticBag diagnostics)
    {
        if(captures.Count == 0)
        {
            return inner();
        }

        var sequence = new Sequence();
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

        try
        {
            foreach(var step in sequence.Steps)
            {
                if(step.Capture is { } capture)
                {
                    _ = opened.Append($"({Evaluation(capture, SwitchPrecedence + 1, diagnostics)} switch {{ _ => ");
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