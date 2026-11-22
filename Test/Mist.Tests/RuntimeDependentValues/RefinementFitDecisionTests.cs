using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// The fit judgement of runtime-dependent-values Requirement 2 over a hand-built state, for each kind of
/// target slot: what is provable about the source's slot, read through the value's own holder, must lie
/// inside the target's refinement, or equal the one value a literal, a dependent parameter or an exact
/// capture stands for.
/// </summary>
[Trait("Tier", "1")]
public sealed class RefinementFitDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly FactReading reading = new(new LiftedReturns());
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly RefinedType range;
    private readonly LocalVariable n;
    private readonly SlotCapture written;
    private readonly LocalVariable some;
    private readonly Value held;
    private readonly Value read;

    public RefinementFitDecisionTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        range = slots.Refinements.Between(slots.Int, 11L, 29L, "Range");
        n = new LocalVariable("n", range);
        written = slots.Capture(n, slots.Int);
        some = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        held = new Value(some.Type, "ring", HandBuiltModel.Node());
        read = new Value(slots.Int, "ring.Capacity", HandBuiltModel.Node());
    }

    private FactState Holding(params Fact[] facts)
    {
        var state = FactState.Empty with
        {
            Bindings = Bindings.Empty.Set(some, held).Set(new MemberHolder(held, capacity), read),
            Kept = ImmutableDictionary<SlotCapture, IntegralSet>.Empty.Add(written, Between(11, 29)),
        };

        return state with { Bag = facts.Aggregate(state.Bag, (bag, fact) => bag.Add(fact)) };
    }

    private static IntegralSet Between(long low, long high)
    {
        return new IntegralSet([new IntegralInterval(low, high)]);
    }

    private Fact Within(long low, long high)
    {
        return Fact.In(read, Between(low, high));
    }

    private UnprovenSlot? Judge(FactState state, IMistType target, SlotMeeting? meeting = null)
    {
        return SlotIdentity.Judge(state, HandBuiltRefinements.Read(some), target, reading, meeting: meeting);
    }

    private static string? Case(UnprovenSlot? unproven)
    {
        return unproven?.Proof is Refuted refuted ? FailingCase.Describe(refuted) : null;
    }

    [Fact]
    public void ALiteralTargetNeedsTheReadToBeThatValue()
    {
        Assert.Null(Judge(Holding(new Fact(new Term(read).Subtract(new Term(15)), Relation.Equal)), slots.Closed(ring, slots.Literal(15))));

        var unproven = Judge(Holding(Within(11, 29)), slots.Closed(ring, slots.Literal(15)));

        Assert.Null(unproven?.Refinement);
        Assert.Equal(("ring.Capacity", "15"), (unproven?.Left, unproven?.Right));
        Assert.NotNull(Case(unproven));
    }

    [Fact]
    public void ACaptureTargetAdmitsEveryReadInsideTheRefinementItKept()
    {
        Assert.Null(Judge(Holding(Within(12, 20)), slots.Closed(ring, written.Value)));

        var unproven = Judge(Holding(Within(11, 40)), slots.Closed(ring, written.Value));

        Assert.Equal("int >= 11 and <= 29", unproven?.Refinement);
        Assert.StartsWith("fails when ring.Capacity >= 30", Case(unproven), StringComparison.Ordinal);
    }

    [Fact]
    public void ARefinementArgumentTargetAdmitsEveryReadItsPredicateAdmits()
    {
        var target = slots.Closed(ring, slots.Refinements.Built.Interner.GetOrCreateRefinement(slots.Int, range, null));

        Assert.Null(Judge(Holding(Within(11, 29)), target));

        var unproven = Judge(Holding(), target);

        Assert.Equal("int >= 11 and <= 29", unproven?.Refinement);
        Assert.NotNull(Case(unproven));
    }

    [Fact]
    public void AnUnknownTargetOwesNothingWhereACaptureTargetRefusesTheSameRead()
    {
        Assert.Null(Judge(Holding(), slots.Closed(ring, UnknownType.Instance)));
        Assert.NotNull(Judge(Holding(), slots.Closed(ring, written.Value)));
    }

    [Fact]
    public void ADeclarationsDependentParameterIsMetByEqualityNotByItsDomain()
    {
        var parameter = slots.Dependent("N");
        var bound = new Value(slots.Int, "N", HandBuiltModel.Node());
        var state = Holding(Within(11, 29)) with { Bindings = Holding().Bindings.Set(parameter, bound) };

        Assert.Null(Judge(state with { Bag = state.Bag.Add(new Fact(new Term(read).Subtract(new Term(bound)), Relation.Equal)) }, slots.Closed(ring, parameter)));

        var unproven = Judge(state, slots.Closed(ring, parameter));

        Assert.Null(unproven?.Refinement);
        Assert.Equal(("ring.Capacity", "N"), (unproven?.Left, unproven?.Right));
    }

    [Fact]
    public void AnExactCaptureIsMetByEqualityWithItsValueWhereARefinementWouldAdmit()
    {
        var value = new Value(slots.Int, "n", HandBuiltModel.Node());
        var state = Holding(Within(11, 29)) with { Bindings = Holding().Bindings.Set(written, value) };

        Assert.Null(Judge(state, slots.Closed(ring, written.Value)));
        Assert.NotNull(Judge(state, slots.Closed(ring, written.Value), new SlotMeeting([written])));
        Assert.Null(Judge(
            state with { Bag = state.Bag.Add(new Fact(new Term(read).Subtract(new Term(value)), Relation.Equal)) },
            slots.Closed(ring, written.Value), new SlotMeeting([written])));
    }

    [Fact]
    public void ANestedPairIsMetByRefinementEquality()
    {
        var list = HandBuiltModel.Class("List<>");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        var other = slots.Capture(n, slots.Int);
        var narrower = slots.Capture(n, slots.Int);
        var state = Holding() with { Kept = Holding().Kept.Add(other, Between(11, 29)).Add(narrower, Between(11, 19)) };
        var source = slots.Closed(list, slots.Closed(ring, written.Value));

        Assert.Null(SlotIdentity.Judge(state, source, slots.Closed(list, slots.Closed(ring, other.Value)), through: null, reading));
        Assert.NotNull(SlotIdentity.Judge(state, source, slots.Closed(list, slots.Closed(ring, narrower.Value)), through: null, reading));
        Assert.NotNull(SlotIdentity.Judge(state, source, slots.Closed(list, slots.Closed(ring, slots.Literal(15))), through: null, reading));
    }
}