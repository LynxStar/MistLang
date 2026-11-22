using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// The refinement a capture keeps at its Declare step (runtime-dependent-values Requirement 1.4): every bound
/// the chain proves of that one value, with every other value eliminated, fixed for the life of the type; and
/// a slot read through a value after a write to it, which reads the value's own holder and not the capture.
/// </summary>
[Trait("Tier", "1")]
public sealed class KeptRefinementDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly FactReading reading = new(new LiftedReturns());
    private readonly LocalVariable n;
    private readonly SlotCapture capture;
    private readonly Value value;

    public KeptRefinementDecisionTests()
    {
        n = new LocalVariable("n", slots.Int);
        capture = slots.Capture(n, slots.Int);
        value = new Value(slots.Int, "n", HandBuiltModel.Node());
    }

    private FactState Declared(params Fact[] facts)
    {
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(n, value).Set(capture, value) };
        return state with { Bag = facts.Aggregate(state.Bag.Add(Fact.In(value, Between(11, 29))), (bag, fact) => bag.Add(fact)) };
    }

    private static IntegralSet Between(long low, long high)
    {
        return new IntegralSet([new IntegralInterval(low, high)]);
    }

    private IntegralSet Kept(FactState state)
    {
        return SlotReads.Keep(state, capture).Kept[capture];
    }

    [Fact]
    public void WithNoPathConditionTheRefinementIsTheValuesBounds()
    {
        Assert.Equal(Between(11, 29), Kept(Declared()));
    }

    [Fact]
    public void APathConditionNarrowsTheRefinement()
    {
        Assert.Equal(Between(21, 29), Kept(Declared(new Fact(new Term(20).Subtract(new Term(value)), Relation.LessThan))));
    }

    [Fact]
    public void ARelationToAnotherValueStaysOutAndALaterFactLeavesTheRefinementAsKept()
    {
        var m = new Value(slots.Int, "m", HandBuiltModel.Node());
        var kept = SlotReads.Keep(Declared(new Fact(new Term(value).Subtract(new Term(m)), Relation.LessThan)), capture);
        var later = kept with { Bag = kept.Bag.Add(new Fact(new Term(value).Subtract(new Term(15)), Relation.Equal)) };

        Assert.Equal(Between(11, 29), kept.Kept[capture]);
        Assert.Equal(Between(11, 29), later.Kept[capture]);
        Assert.Equal(Admitted.Between(11, 29), SlotReads.Refinement(later, capture.Value));
    }

    [Fact]
    public void ARelationTheChainResolvesToABoundNarrowsTheRefinement()
    {
        var k = new Value(slots.Int, "k", HandBuiltModel.Node());

        Assert.Equal(Between(11, 15), Kept(Declared(
            new Fact(new Term(value).Subtract(new Term(k)).Subtract(new Term(2))),
            new Fact(new Term(k).Subtract(new Term(13))))));
    }

    [Fact]
    public void ASlotReadThroughAWrittenVariableReadsTheNewValuesHolderAndNotTheCapture()
    {
        var ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        var capacity = ring.TypeParametersList[0];
        var cell = new LocalVariable("cell", slots.Closed(ring, capture.Value));
        var (first, second, made) = (new Value(cell.Type, "cell", HandBuiltModel.Node()), new Value(cell.Type, "cell", HandBuiltModel.Node()), new Value(slots.Int, "m", HandBuiltModel.Node()));
        var created = SlotReads.Keep(Declared(), capture) with
        {
            Bindings = Declared().Bindings.Set(cell, first).Set(new MemberHolder(first, capacity), value),
        };
        var rewritten = created with { Bindings = created.Bindings.Set(cell, second).Set(new MemberHolder(second, capacity), made) };

        Assert.Equal(new Term(value), SlotReads.Value(ref created, reading, capture.Value, HandBuiltRefinements.Read(cell), capacity)!);
        Assert.Equal(new Term(made), SlotReads.Value(ref rewritten, reading, capture.Value, HandBuiltRefinements.Read(cell), capacity)!);

        var unwritten = created with { Bindings = created.Bindings.Set(cell, new Value(cell.Type, "cell", HandBuiltModel.Node())) };
        var fresh = SlotReads.Value(ref unwritten, reading, capture.Value, HandBuiltRefinements.Read(cell), capacity)!;

        Assert.NotEqual(new Term(value), fresh);
        Assert.Equal(Between(11, 29), unwritten.Bag.Intervals(fresh.Variables[0]));
    }
}