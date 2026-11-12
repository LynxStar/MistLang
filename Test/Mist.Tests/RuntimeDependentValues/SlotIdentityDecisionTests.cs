using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class SlotIdentityDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly SlotCapture first;
    private readonly SlotCapture second;

    public SlotIdentityDecisionTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        first = slots.Capture(new LocalVariable("n", slots.Int));
        second = slots.Capture(new LocalVariable("m", slots.Int));
    }

    private static UnprovenSlot? Judge(FactState state, IMistType source, IMistType target)
    {
        return SlotIdentity.Judge(state, HandBuiltRefinements.Local(source, "ring"), target);
    }

    [Fact]
    public void WithNoFactsOnlyTheSameCaptureIsOneValue()
    {
        Assert.Null(Judge(FactState.Empty, slots.Closed(ring, first.Value), slots.Closed(ring, first.Value)));

        var unproven = Judge(FactState.Empty, slots.Closed(ring, first.Value), slots.Closed(ring, second.Value));

        Assert.Equal(("n", "m"), (unproven?.Left, unproven?.Right));
    }

    [Fact]
    public void TwoCapturesAreOneValueOnlyWhereTheFlowBoundThemToOne()
    {
        var held = new Value(slots.Int, "n", HandBuiltModel.Node());
        var one = FactState.Empty with { Bindings = Bindings.Empty.Set(first, held).Set(second, held) };
        var two = FactState.Empty with { Bindings = Bindings.Empty.Set(first, held).Set(second, new Value(slots.Int, "m", HandBuiltModel.Node())) };

        Assert.Null(Judge(one, slots.Closed(ring, first.Value), slots.Closed(ring, second.Value)));
        Assert.NotNull(Judge(two, slots.Closed(ring, first.Value), slots.Closed(ring, second.Value)));
    }

    [Fact]
    public void ASlotTheTargetLeavesUnknownOwesNothing()
    {
        Assert.Null(Judge(FactState.Empty, slots.Closed(ring, first.Value), slots.Closed(ring, UnknownType.Instance)));
        Assert.Null(Judge(FactState.Empty, slots.ArrayOf(first.Value), slots.ArrayOf(new UnknownValueType(slots.Int))));
        Assert.Null(Judge(FactState.Empty, slots.Closed(ring, first.Value), slots.Int));
    }

    [Fact]
    public void AnUnknownSlotIsShownAsTheReadThroughTheValue()
    {
        var unproven = Judge(FactState.Empty, slots.ArrayOf(new UnknownValueType(slots.Int)), slots.ArrayOf(slots.Literal(3)));

        Assert.Equal(("ring.Length", "3"), (unproven?.Left, unproven?.Right));
    }

    [Fact]
    public void AClosingIsJudgedThroughTheBaseItReachesTheTargetBy()
    {
        var parameter = new TypeParameter("Capacity", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var wide = HandBuiltModel.Class("Wide<>", slots.Closed(ring, parameter));
        wide.TypeParametersList.Add(parameter);

        Assert.Null(Judge(FactState.Empty, slots.Closed(wide, first.Value), slots.Closed(ring, first.Value)));
        Assert.NotNull(Judge(FactState.Empty, slots.Closed(wide, first.Value), slots.Closed(ring, second.Value)));
    }

    [Fact]
    public void AnOptionalTargetIsJudgedByTheClosingItHolds()
    {
        var optional = new UnionType([slots.Closed(ring, second.Value), slots.Refinements.Built.Type("Void")]);

        Assert.NotNull(Judge(FactState.Empty, slots.Closed(ring, first.Value), optional));
        Assert.Null(Judge(FactState.Empty, slots.Closed(ring, second.Value), optional));
    }
}