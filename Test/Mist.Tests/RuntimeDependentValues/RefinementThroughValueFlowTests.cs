using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// Where a slot stands for one value rather than a refinement, judged by the refinement analyzer over
/// hand-built bodies: a <c>ref</c> argument against a call's own capture (runtime-dependent-values
/// Requirements 2.5 and 8.4), and a member reached through a value, a call's result, an element or an
/// indexer, read or written, or a method group converted to a function type, whose signature reads that
/// value's own slot (Requirement 7.6).
/// </summary>
[Trait("Tier", "2")]
public sealed class RefinementThroughValueFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly MemberType list;
    private readonly RefinedType range;
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public RefinementThroughValueFlowTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        list = HandBuiltModel.Class("List<>");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        range = slots.Refinements.Between(slots.Int, 11L, 29L, "Range");
        n = new LocalVariable("n", range);
        m = new LocalVariable("m", range);
    }

    private IMistType Void => slots.Refinements.Built.Type("Void");

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private static void Refused(IReadOnlyList<Diagnostic> diagnostics, string message)
    {
        var refused = Assert.Single(diagnostics);
        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    private (LocalVariable Q, LocalDeclarationStatement Declared) RingOfM()
    {
        var written = slots.Capture(n, slots.Int);
        var made = slots.Capture(m, slots.Int);
        var q = new LocalVariable("q", slots.Closed(ring, written.Value));
        var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, made.Value), [], null, [], []) { Captures = [made] };
        return (q, HandBuiltSlots.Declare(q, creation, written));
    }

    private (IndexExpression Element, SlotCapture Written) ElementOfN()
    {
        var written = slots.Capture(n, slots.Int);
        var rings = new LocalVariable("list", slots.Closed(list, slots.Closed(ring, written.Value)));
        return (new IndexExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(rings), slots.Refinements.Literal(0), slots.Closed(ring, written.Value)), written);
    }

    private static SlotCapture Own(LocalVariable receiver, TypeParameter parameter, IMistType domain, int position)
    {
        var read = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(receiver), parameter.Name, domain, member: new TypeResult(parameter));
        return new SlotCapture($"{receiver.Name}.{parameter.Name}", domain, domain, read, position);
    }

    private ExpressionStatement Load(Expression receiver, IMistType taken, LocalVariable length)
    {
        var method = new Method("Load", Void, [new Parameter("items", taken)], new MemberModifiers()) { DeclaringType = ring };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Load", Void);
        return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [slots.Create(slots.Capture(length))], Void, method, [taken]));
    }

    [Fact]
    public void ARefArgumentMeetsACallsOwnCaptureByItsValueAndNotOnlyByItsRefinement()
    {
        ExpressionStatement swap(LocalVariable target, SlotCapture stated)
        {
            var taken = slots.ArrayOf(stated.Value);
            var byRef = new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref);
            var method = new Method("Swap", Void, [new Parameter("b", taken, byRef)], new MemberModifiers());
            return HandBuiltRefinements.Evaluate(new InvocationExpression(
                HandBuiltModel.Node(), slots.Refinements.Literal(0L), [HandBuiltRefinements.Read(target)], Void, method, [taken])
            {
                Captures = [stated],
            });
        }

        var b = new LocalVariable("b", slots.ArrayOf(slots.Capture(m).Value));
        var declared = new LocalDeclarationStatement(b, slots.Create(slots.Capture(m)));

        Refused(Analyze(declared, swap(b, slots.Capture(n, slots.Int))), "are not proven identical");

        var fifteen = new LocalVariable("fifteen", slots.Int);
        var c = new LocalVariable("c", slots.ArrayOf(slots.Literal(15)));
        var given = new LocalDeclarationStatement(fifteen, slots.Refinements.Literal(15L));

        Assert.Empty(Analyze(given, swap(c, slots.Capture(fifteen, slots.Int))));
    }

    [Fact]
    public void AMemberReachedThroughACallsResultTakesAnArgumentEqualToThatResultsOwnSlot()
    {
        var (q, declared) = RingOfM();
        var own = Own(q, capacity, slots.Int, 91);
        var copied = slots.Closed(ring, own.Value);
        var copy = new Method("Copy", copied, [], new MemberModifiers()) { DeclaringType = ring };
        var result = new InvocationExpression(
            HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(q), "Copy", copied), [], copied, copy, [])
        {
            Captures = [own],
        };

        Assert.Empty(Analyze(declared, Load(result, slots.ArrayOf(own.Value), m)));
        Refused(Analyze(declared, Load(result, slots.ArrayOf(own.Value), n)), "'n' and 'Ring<q.Capacity>.Capacity' are not proven identical (fails when n <= m - 1)");
    }

    [Fact]
    public void AMemberReachedThroughAnElementTakesNothingTheElementsOwnSlotIsNotProvenToEqual()
    {
        var (element, written) = ElementOfN();

        Refused(Analyze(Load(element, slots.ArrayOf(written.Value), n)), "'n' and 'Ring<n>.Capacity' are not proven identical");
    }

    [Fact]
    public void AnIndexerReachedThroughAValueTakesAKeyEqualToThatValuesOwnSlot()
    {
        var (q, declared) = RingOfM();
        var indexer = new Indexer(slots.Int, [new Parameter("key", slots.ArrayOf(capacity))], new MemberModifiers()) { DeclaringType = ring };

        ExpressionStatement at(Expression receiver, IMistType key, LocalVariable length, params SlotCapture[] captures)
        {
            return HandBuiltRefinements.Evaluate(new IndexExpression(HandBuiltModel.Node(), receiver, slots.Create(slots.Capture(length)), slots.Int)
            {
                Indexer = indexer,
                ParameterType = key,
                Captures = captures,
            });
        }

        var own = Own(q, capacity, slots.Int, 92);

        Assert.Empty(Analyze(declared, at(HandBuiltRefinements.Read(q), slots.ArrayOf(own.Value), m, own)));
        Refused(Analyze(declared, at(HandBuiltRefinements.Read(q), slots.ArrayOf(own.Value), n, own)), "'n' and 'q.Capacity' are not proven identical (fails when n <= m - 1)");

        var (element, written) = ElementOfN();

        Refused(Analyze(at(element, slots.ArrayOf(written.Value), n)), "'n' and 'Ring<n>.Capacity' are not proven identical");
    }

    [Fact]
    public void AnIndexerWrittenThroughAValueTakesAValueEqualToThatValuesOwnSlot()
    {
        var (q, declared) = RingOfM();
        var indexer = new Indexer(slots.ArrayOf(capacity), [new Parameter("at", slots.Int)], new MemberModifiers()) { DeclaringType = ring };

        ExpressionStatement write(Expression receiver, IMistType element, LocalVariable length, params SlotCapture[] captures)
        {
            var target = new IndexExpression(HandBuiltModel.Node(), receiver, slots.Refinements.Literal(0), element)
            {
                Indexer = indexer,
                ParameterType = slots.Int,
                Captures = captures,
            };

            return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(target, slots.Create(slots.Capture(length))));
        }

        var own = Own(q, capacity, slots.Int, 93);

        Assert.Empty(Analyze(declared, write(HandBuiltRefinements.Read(q), slots.ArrayOf(own.Value), m, own)));
        Refused(Analyze(declared, write(HandBuiltRefinements.Read(q), slots.ArrayOf(own.Value), n, own)), "'n' and 'q.Capacity' are not proven identical (fails when n <= m - 1)");

        var (element, written) = ElementOfN();

        Refused(Analyze(write(element, slots.ArrayOf(written.Value), n)), "'n' and 'Ring<n>.Capacity' are not proven identical");
    }

    [Fact]
    public void APropertyWrittenThroughAValueTakesAValueEqualToThatValuesOwnSlot()
    {
        var (q, declared) = RingOfM();
        var peer = new Property("Peer", slots.Closed(ring, capacity), new MemberModifiers()) { DeclaringType = ring };

        ExpressionStatement write(LocalVariable value)
        {
            var own = Own(q, capacity, slots.Int, 94);
            var target = new MemberAccessExpression(
                HandBuiltModel.Node(), HandBuiltRefinements.Read(q), "Peer", slots.Closed(ring, own.Value), member: new PropertyResult(peer, ring, []))
            {
                Captures = [own],
            };
            var made = slots.Capture(value, slots.Int);
            var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, made.Value), [], null, [], []) { Captures = [made] };

            return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(target, creation));
        }

        Assert.Empty(Analyze(declared, write(m)));
        Refused(Analyze(declared, write(n)), "'n' and 'q.Capacity' are not proven identical (fails when n <= m - 1)");
    }

    [Fact]
    public void AMethodGroupReachedThroughAValueConvertsOnlyToATypeWhoseSlotIsThatValuesOwn()
    {
        var (q, declared) = RingOfM();
        var load = new Method("Load", Void, [new Parameter("items", slots.ArrayOf(capacity))], new MemberModifiers()) { DeclaringType = ring };

        LocalDeclarationStatement group(Expression receiver, IMistType taken, IMistType expected, SlotCapture[] captures, params SlotCapture[] written)
        {
            var target = slots.Refinements.Built.Interner.GetOrCreateFunctionType([slots.ArrayOf(expected)], Void);
            var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, load, target)
            {
                Captures = captures,
                ClosedParameterTypes = [slots.ArrayOf(taken)],
                ClosedReturnType = Void,
            };

            return HandBuiltSlots.Declare(new LocalVariable("g", target), reference, written);
        }

        var own = Own(q, capacity, slots.Int, 95);
        var fifteen = slots.Refinements.Binary(HandBuiltRefinements.Read(m), BinaryOperatorKind.Equal, slots.Refinements.Literal(15L));
        var ofM = slots.Capture(m, slots.Int);

        Assert.Empty(Analyze(declared, HandBuiltRefinements.If(fifteen, group(HandBuiltRefinements.Read(q), own.Value, slots.Literal(15), [own]))));
        Refused(Analyze(declared, group(HandBuiltRefinements.Read(q), own.Value, slots.Literal(15), [own])), "'15' and 'q.Capacity' are not proven identical (fails when m <= 14");
        Refused(Analyze(declared, group(HandBuiltRefinements.Read(q), own.Value, ofM.Value, [own], ofM)), "'m' and 'q.Capacity' are not proven identical");

        var (element, written) = ElementOfN();

        Refused(Analyze(group(element, written.Value, written.Value, [])), "'n' and 'Ring<n>.Capacity' are not proven identical");
    }
}