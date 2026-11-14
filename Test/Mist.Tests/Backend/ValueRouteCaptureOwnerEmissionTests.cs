using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// Every owner of a capture evaluates an initializer that runs code exactly once, where the type is written, whether a
/// hidden argument consumes it or nothing does (runtime-dependent-values Requirement 1; MIST-1369): a call with a
/// stated argument, a call through a closed type, a typed for initializer, a type test that fails, a switch arm and an
/// <c>as</c>. A receiver written before the type runs before it, and a capture that is a plain read emits nothing. A
/// struct receiver, an assignment's target and an argument passed by reference keep their place, and a call that
/// yields nothing takes its capture as a statement.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteCaptureOwnerEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType bag;
    private readonly ClassType counter;
    private readonly SlotCapture capture;
    private readonly ConstructedType closing;
    private readonly LocalReferenceExpression x = HandBuiltRefinements.Local(UnknownType.Instance, "x");

    public ValueRouteCaptureOwnerEmissionTests()
    {
        ring = Ring(slots, out capacity);
        bag = Generic("Bag<>");
        counter = Generic("Counter<>");
        capture = Capture(slots, Count(slots));
        closing = slots.Closed(ring, capture.Value);
    }

    [Fact]
    public void ACallOwningACaptureNothingConsumesEvaluatesItBeforeItsArguments()
    {
        var t = new TypeParameter("T", 0, new Scope(null), null);
        var first = Static("First", slots.Int, new Parameter("items", slots.Closed(bag, t)));
        first.TypeParametersList.Add(t);
        t.DeclaringMethod = first;
        using var routes = SlotRoutes.Enter([capacity]);
        var items = HandBuiltRefinements.Local(slots.Closed(bag, closing), "bag");
        var call = Call(first, [closing], [items], [slots.Closed(bag, closing)], captures: [capture]);

        Assert.Equal("(p.Count switch { _ => First<Probe.Ring>(bag) })", Emit(call));
        Assert.Equal(["_ = p.Count;", "First<Probe.Ring>(bag);"], Statements(HandBuiltRefinements.Evaluate(call)));
    }

    [Fact]
    public void AReceiverWrittenBeforeTheTypeRunsBeforeIt()
    {
        var inner = HandBuiltModel.Class("Inner");
        inner.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var t = new TypeParameter("T", 0, inner.Scope, null);
        var inst = new Method("Inst", slots.Int, [new Parameter("items", slots.Closed(bag, t))], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = inner, Scope = inner.Scope };
        inst.TypeParametersList.Add(t);
        t.DeclaringMethod = inst;
        var make = Static("Make", inner);
        using var routes = SlotRoutes.Enter([capacity]);
        var items = HandBuiltRefinements.Local(slots.Closed(bag, closing), "bag");

        Assert.Equal(
            "(Make() switch { var __e1 => (p.Count switch { _ => __e1.Inst<Probe.Ring>(bag) }) })",
            Emit(Through(Call(make, [], [], []), inst, [closing], [items], [slots.Closed(bag, closing)], captures: [capture])));
        Assert.Equal(
            ["Inner __e1 = Make();", "_ = p.Count;", "__e1.Inst<Probe.Ring>(bag);"],
            Statements(HandBuiltRefinements.Evaluate(Through(Call(make, [], [], []), inst, [closing], [items], [slots.Closed(bag, closing)], captures: [capture]))));
    }

    [Fact]
    public void ACallThroughAClosedTypeEvaluatesTheClosingsCapture()
    {
        var counter = Generic("Counter<>");
        var read = new Method("Read", slots.Int, [], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public }) { DeclaringType = counter, Scope = counter.Scope };
        using var routes = SlotRoutes.Enter([capacity]);
        var through = new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(counter, closing));

        Assert.Equal("(p.Count switch { _ => Probe.Counter<Probe.Ring>.Read() })", Emit(Through(through, read, [], [], [], captures: [capture])));
    }

    [Fact]
    public void ACallThroughAValueRoutedClosingConsumesTheCaptureAsTheHiddenArgument()
    {
        var read = new Method("Read", slots.Int, [], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public }) { DeclaringType = ring, Scope = ring.Scope };
        using var routes = SlotRoutes.Enter([capacity]);
        var through = new TypeReferenceExpression(HandBuiltModel.Node(), closing);

        Assert.Equal("Probe.Ring.Read(p.Count)", Emit(Through(through, read, [], [], [], captures: [capture])));
    }

    [Fact]
    public void ATypedForInitializerEvaluatesItsCaptureOnceBeforeTheLoop()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var initializer = new LocalDeclarationStatement(new LocalVariable("r", closing), HandBuiltRefinements.Local(closing, "other")) { Captures = [capture] };
        var loop = new ForStatement(initializer, HandBuiltModel.Literal(slots.Refinements.Bool, false), null, HandBuiltModel.Block());

        Assert.Equal(["_ = p.Count;", "for (Probe.Ring r = other; false; )", "{", "}"], Statements(loop));
    }

    [Fact]
    public void ATestEvaluatesItsCaptureBetweenTheScrutineeAndTheTypeTest()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal(
            "(x is var __e1 && p.Count is var __e2 && __e1 is Probe.Ring r && r.__Capacity == __e2)",
            Emit(Is(slots, x, Tested(closing, "r", capture))));
        Assert.Equal(
            "x switch { var __e1 when p.Count is var __e2 && __e1 is Probe.Ring r && r.__Capacity == __e2 => 1, var _ => 0 }",
            Emit(Switch(slots, x, Tested(closing, "r", capture))));
    }

    [Fact]
    public void ATestTheScrutineeAdmitsStillEvaluatesItsCapture()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("(r is var __e1 && p.Count is var __e2 && __e1 is Probe.Ring)", Emit(Is(slots, HandBuiltRefinements.Local(closing, "r"), Tested(closing, null, capture))));
    }

    [Fact]
    public void AnAsEvaluatesItsCaptureBetweenTheOperandAndTheTest()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var conversion = new AsExpression(
            HandBuiltModel.Node(7), x, closing, AsConversionKind.Narrowing, null, new UnionType([closing, slots.Refinements.Built.Type("Void")]))
        { Captures = [capture] };

        Assert.Equal("(x is var __e1 && p.Count is var __e2 && __e1 is Probe.Ring __as7 && __as7.__Capacity == __e2 ? __as7 : default(Probe.Ring?))", Emit(conversion));
    }

    [Fact]
    public void ACallYieldingNothingEvaluatesItsCaptureAsAStatementInALambdaAndAForInitializer()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var nothing = slots.Refinements.Built.Type("Void");
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [], Through(Closed(), Counted("Do", nothing), [], [], [], captures: [capture]), null, false, new FunctionType([], nothing));
        var loop = new ForStatement(
            HandBuiltRefinements.Evaluate(Through(Closed(), Counted("Do", nothing), [], [], [], captures: [capture])),
            HandBuiltModel.Literal(slots.Refinements.Bool, false), null, HandBuiltModel.Block());

        Assert.Equal(["() =>", "{", "_ = p.Count;", "Probe.Counter<Probe.Ring>.Do();", "}"], Lines(Emit(lambda)));
        Assert.Equal(["{", "_ = p.Count;", "Probe.Counter<Probe.Ring>.Do();", "while (true)"], Statements(loop).Take(4));
        Assert.False(BodyEmitter.NeedsStatements(Through(Closed(), Counted("Read", slots.Int), [], [], [], captures: [capture]), discarded: false));
    }

    [Fact]
    public void AnAssignmentThroughAClosedTypeEvaluatesTheCaptureAheadAndKeepsItsTarget()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("(p.Count switch { _ => Probe.Counter<Probe.Ring>.Prop = 3 })", Emit(HandBuiltRefinements.Assign(Prop(), HandBuiltModel.Literal(slots.Int, 3L))));
        Assert.Equal(
            ["_ = p.Count;", "Probe.Counter<Probe.Ring>.Prop = 3;"],
            Statements(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(Prop(), HandBuiltModel.Literal(slots.Int, 3L)))));
        Assert.Equal(["_ = p.Count;", "Probe.Counter<Probe.Ring>.Prop++;"], Statements(HandBuiltRefinements.Evaluate(Increment(Prop()))));
        Assert.Equal(["_ = p.Count;", "Probe.Counter<Probe.Ring>.Prop += 3;"], Statements(HandBuiltRefinements.Evaluate(Add(Prop()))));
    }

    [Fact]
    public void AStructReceiverInAnElementOrAFieldIsReadWhereItLiesAndOnlyWhatLocatesItIsBound()
    {
        var (tally, bump) = Tally();
        var store = HandBuiltModel.Class("Store");
        var field = new Field("tally", tally, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = store };
        var make = Static("Make", store);
        var index = Static("Idx", slots.Int);
        using var routes = SlotRoutes.Enter([capacity]);

        Expression element()
        {
            return new IndexExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(UnknownType.Instance, "ts"), Call(index, [], [], []), tally);
        }

        Assert.Equal(
            "(Idx() switch { var __e1 => (p.Count switch { _ => ts[__e1].Bump<Probe.Ring>() }) })",
            Emit(Through(element(), bump, [closing], [], [], captures: [capture])));
        Assert.Equal(
            ["int __e1 = Idx();", "_ = p.Count;", "ts[__e1].Bump<Probe.Ring>();"],
            Statements(HandBuiltRefinements.Evaluate(Through(element(), bump, [closing], [], [], captures: [capture]))));
        Assert.Equal(
            "(Make() switch { var __e1 => (p.Count switch { _ => __e1.tally.Bump<Probe.Ring>() }) })",
            Emit(Through(
                new MemberAccessExpression(HandBuiltModel.Node(), Call(make, [], [], []), "tally", tally, member: new FieldResult(field, store, [])),
                bump, [closing], [], [], captures: [capture])));
    }

    [Fact]
    public void AReceiverReturnedByReferenceRunsInPlaceAndTheCallsCaptureFollowsIt()
    {
        var (tally, bump) = Tally();
        var store = HandBuiltModel.Class("Store");
        var slot = Returning(store, "TallySlot", tally);
        var second = Capture(slots, Count(slots, "q"), "q.Count");
        using var routes = SlotRoutes.Enter([capacity]);
        var st = HandBuiltRefinements.Local(store, "st");
        var returned = Through(st, slot, [closing], [], [], captures: [capture]);

        Assert.Equal(
            "(p.Count switch { _ => System.Runtime.CompilerServices.Unsafe.Add(ref st.TallySlot<Probe.Ring>(), (q.Count switch { _ => 0 })).Bump<Probe.Ring>() })",
            Emit(Through(returned, bump, [slots.Closed(ring, second.Value)], [], [], captures: [second])));
        Assert.Equal(
            ["System.Runtime.CompilerServices.Unsafe.Add(ref st.TallySlot<int>(), (q.Count switch { _ => 0 })).Bump<Probe.Ring>();"],
            Statements(HandBuiltRefinements.Evaluate(Through(Through(st, slot, [slots.Int], [], []), bump, [slots.Closed(ring, second.Value)], [], [], captures: [second]))));
    }

    [Fact]
    public void ACallReturningAReferenceKeepsItsPlaceAsATargetAndAsAnArgumentPassedByReference()
    {
        var store = HandBuiltModel.Class("Store");
        var slot = Returning(store, "Slot", slots.Int);
        var arg = Static("Arg", slots.Int);
        var add = Static("Add", slots.Refinements.Built.Type("Void"), new Parameter("by", slots.Int), new Parameter("slot", slots.Int, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)));
        using var routes = SlotRoutes.Enter([capacity]);
        var st = HandBuiltRefinements.Local(store, "st");

        InvocationExpression returned()
        {
            return Through(st, slot, [closing], [], [], captures: [capture]);
        }

        Assert.Equal(["_ = p.Count;", "st.Slot<Probe.Ring>() = 3;"], Statements(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(returned(), HandBuiltModel.Literal(slots.Int, 3L)))));
        Assert.Equal(["_ = p.Count;", "st.Slot<Probe.Ring>()++;"], Statements(HandBuiltRefinements.Evaluate(Increment(returned()))));
        Assert.Equal(
            ["int __e1 = Arg();", "_ = p.Count;", "Add(__e1, ref st.Slot<Probe.Ring>());"],
            Statements(HandBuiltRefinements.Evaluate(Call(add, [], [Call(arg, [], [], []), returned()], [slots.Int, slots.Int]))));
    }

    [Fact]
    public void APlainReadNothingConsumesEmitsNothing()
    {
        var t = new TypeParameter("T", 0, new Scope(null), null);
        var first = Static("First", slots.Int, new Parameter("items", slots.Closed(bag, t)));
        first.TypeParametersList.Add(t);
        t.DeclaringMethod = first;
        var plain = slots.Capture(new LocalVariable("n", slots.Int));
        var closed = slots.Closed(ring, plain.Value);
        using var routes = SlotRoutes.Enter([capacity]);
        var items = HandBuiltRefinements.Local(slots.Closed(bag, closed), "bag");

        Assert.Equal("First<Probe.Ring>(bag)", Emit(Call(first, [closed], [items], [slots.Closed(bag, closed)], captures: [plain])));
    }

    private TypeReferenceExpression Closed()
    {
        return new TypeReferenceExpression(HandBuiltModel.Node(), slots.Closed(counter, closing));
    }

    private Method Counted(string name, IMistType returns)
    {
        return new Method(name, returns, [], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public }) { DeclaringType = counter, Scope = counter.Scope };
    }

    private MemberAccessExpression Prop()
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), Closed(), "Prop", slots.Int) { Captures = [capture] };
    }

    private CompoundAssignmentExpression Increment(Expression target)
    {
        return new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, HandBuiltModel.Literal(slots.Int, 1L), slots.Int, null, IncrementForm.Postfix);
    }

    private CompoundAssignmentExpression Add(Expression target)
    {
        return new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, HandBuiltModel.Literal(slots.Int, 3L), slots.Int, null, IncrementForm.None);
    }

    private (StructType Tally, Method Bump) Tally()
    {
        var tally = HandBuiltModel.Struct("Tally");
        tally.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var t = new TypeParameter("T", 0, tally.Scope, null);
        var bump = new Method("Bump", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = tally, Scope = tally.Scope };
        bump.TypeParametersList.Add(t);
        t.DeclaringMethod = bump;
        return (tally, bump);
    }

    private static Method Returning(ClassType owner, string name, IMistType returns)
    {
        var t = new TypeParameter("T", 0, owner.Scope, null);
        var method = new Method(name, returns, [], new MemberModifiers { Accessibility = Accessibility.Public, IsRef = true }) { DeclaringType = owner, Scope = owner.Scope };
        method.TypeParametersList.Add(t);
        t.DeclaringMethod = method;
        return method;
    }

    private static List<string> Lines(string text)
    {
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())];
    }
}