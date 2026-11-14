using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// A test against a runtime closing under a combinator (runtime-dependent-values design, <em>Identity by value: the
/// first delivery</em>): the pattern becomes a boolean expression over the scrutinee bound once, each test its own
/// C# test and comparison, and <c>not</c>, <c>or</c> and <c>and</c> the operators they are; a switch arm takes the
/// expression as its guard. A test the scrutinee's own type already admits needs no comparison and keeps the C# pattern.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteCombinatorTestEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly LocalReferenceExpression x = HandBuiltRefinements.Local(UnknownType.Instance, "x");

    public ValueRouteCombinatorTestEmissionTests()
    {
        ring = Ring(slots, out capacity);
    }

    [Fact]
    public void ATestUnderNotBindsTheScrutineeOnceAndNegatesTheTestWithItsComparison()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var negated = new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, Tested(slots.Closed(ring, capture.Value), "r"));

        Assert.Equal("(x is var __e1 && !((__e1 is Probe.Ring r && r.__Capacity == n)))", Emit(Is(slots, x, negated)));
    }

    [Fact]
    public void ATestUnderOrTestsEachAlternativeOnItsOwn()
    {
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        var m = slots.Capture(new LocalVariable("m", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var either = new DisjunctionPattern(
            HandBuiltModel.Node(), UnknownType.Instance, [Tested(slots.Closed(ring, n.Value), null), Tested(slots.Closed(ring, m.Value), null)]);

        Assert.Equal(
            "(x is var __e1 && ((__e1 is Probe.Ring __e2 && __e2.__Capacity == n) || (__e1 is Probe.Ring __e3 && __e3.__Capacity == m)))",
            Emit(Is(slots, x, either)));
    }

    [Fact]
    public void ANestedCombinatorKeepsItsGrouping()
    {
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        var m = slots.Capture(new LocalVariable("m", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var neither = new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, new DisjunctionPattern(
            HandBuiltModel.Node(), UnknownType.Instance, [Tested(slots.Closed(ring, n.Value), null), Tested(slots.Closed(ring, m.Value), null)]));

        Assert.Equal(
            "(x is var __e1 && !(((__e1 is Probe.Ring __e2 && __e2.__Capacity == n) || (__e1 is Probe.Ring __e3 && __e3.__Capacity == m))))",
            Emit(Is(slots, x, neither)));
    }

    [Fact]
    public void AConjunctionHoistsOnlyTheOperandUnderNot()
    {
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        var m = slots.Capture(new LocalVariable("m", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var both = new ConjunctionPattern(
            HandBuiltModel.Node(), UnknownType.Instance,
            [Tested(slots.Closed(ring, n.Value), "r"), new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, Tested(slots.Closed(ring, m.Value), null))]);

        Assert.Equal(
            "(x is Probe.Ring r and var __e1 && r.__Capacity == n && !((__e1 is Probe.Ring __e2 && __e2.__Capacity == m)))",
            Emit(Is(slots, x, both)));
    }

    [Fact]
    public void ASwitchArmUnderNotBindsTheScrutineeAndTestsInItsGuard()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var negated = new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, Tested(slots.Closed(ring, capture.Value), null));

        Assert.Equal(
            "x switch { var __e1 when !((__e1 is Probe.Ring __e2 && __e2.__Capacity == n)) => 1, var _ => 0 }",
            Emit(Switch(slots, x, negated)));
    }

    [Fact]
    public void APropertyPatternHoldingTheTestUnderNotIsNegatedWhole()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var closing = slots.Closed(ring, capture.Value);
        var negated = new NegatedPattern(
            HandBuiltModel.Node(), UnknownType.Instance, new PropertyPattern(HandBuiltModel.Node(), closing, [], null, Tested(closing, null)));

        Assert.Equal("(x is var __e1 && !((__e1 is Probe.Ring { } __e2 && __e2.__Capacity == n)))", Emit(Is(slots, x, negated)));
    }

    [Fact]
    public void ATestAgainstTheScrutineesOwnClosingUnderNotNeedsNoComparison()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity]);
        var closing = slots.Closed(ring, capture.Value);
        var negated = new NegatedPattern(HandBuiltModel.Node(), closing, Tested(closing, null));

        Assert.Equal("r is not Probe.Ring", Emit(Is(slots, HandBuiltRefinements.Local(closing, "r"), negated)));
    }
}