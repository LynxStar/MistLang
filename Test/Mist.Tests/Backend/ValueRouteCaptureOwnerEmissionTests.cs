using Mist.Backends.CSharpTranspiler.Lowering;
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
/// <c>as</c>. A receiver written before the type runs before it, and a capture that is a plain read emits nothing.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteCaptureOwnerEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType bag;
    private readonly SlotCapture capture;
    private readonly ConstructedType closing;
    private readonly LocalReferenceExpression x = HandBuiltRefinements.Local(UnknownType.Instance, "x");

    public ValueRouteCaptureOwnerEmissionTests()
    {
        ring = Ring(slots, out capacity);
        bag = Generic("Bag<>");
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
}