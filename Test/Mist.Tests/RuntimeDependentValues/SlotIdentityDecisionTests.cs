using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class SlotIdentityDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly FactReading reading = new(new LiftedReturns());
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly SlotCapture first;
    private readonly SlotCapture second;
    private readonly Value n;
    private readonly Value m;

    public SlotIdentityDecisionTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        first = slots.Capture(new LocalVariable("n", slots.Int));
        second = slots.Capture(new LocalVariable("m", slots.Int));
        n = new Value(slots.Int, "n", HandBuiltModel.Node());
        m = new Value(slots.Int, "m", HandBuiltModel.Node());
    }

    private FactState Bound()
    {
        return FactState.Empty with { Bindings = Bindings.Empty.Set(first, n).Set(second, m) };
    }

    private static FactState Knowing(FactState state, Fact fact)
    {
        return state with { Bag = state.Bag.Add(fact) };
    }

    private static Fact Equal(Term left, Term right)
    {
        return new Fact(left.Subtract(right), Relation.Equal);
    }

    private static UnprovenSlot? Unflowed(IMistType source, IMistType target)
    {
        return SlotIdentity.Judge(FactState.Empty, HandBuiltRefinements.Local(source, "ring"), target);
    }

    private UnprovenSlot? Judge(FactState state, IMistType source, IMistType target)
    {
        return SlotIdentity.Judge(state, HandBuiltRefinements.Local(source, "ring"), target, reading);
    }

    private static string? Case(UnprovenSlot? unproven)
    {
        return unproven?.Proof is Refuted refuted ? FailingCase.Describe(refuted) : null;
    }

    [Fact]
    public void WithNoFactsOnlyTheSameCaptureIsOneValue()
    {
        Assert.Null(Unflowed(slots.Closed(ring, first.Value), slots.Closed(ring, first.Value)));
        Assert.Null(Unflowed(slots.Closed(ring, slots.Literal(3)), slots.Closed(ring, slots.Literal(3))));

        var unproven = Unflowed(slots.Closed(ring, first.Value), slots.Closed(ring, second.Value));

        Assert.Equal(("n", "m"), (unproven?.Left, unproven?.Right));
        Assert.Null(unproven?.Proof);
    }

    [Fact]
    public void TwoCapturesAreOneValueWhereTheFlowBoundThemToOne()
    {
        var one = FactState.Empty with { Bindings = Bindings.Empty.Set(first, n).Set(second, n) };

        Assert.Null(Judge(one, slots.Closed(ring, first.Value), slots.Closed(ring, second.Value)));

        var unproven = Judge(Bound(), slots.Closed(ring, first.Value), slots.Closed(ring, second.Value));

        Assert.Equal(("n", "m"), (unproven?.Left, unproven?.Right));
        Assert.Equal("fails when n <= m - 1", Case(unproven));
    }

    [Fact]
    public void TheConditionProvesTwoValuesIdentical()
    {
        var proven = Knowing(Bound(), Equal(new Term(n), new Term(m)));

        Assert.Null(Judge(proven, slots.Closed(ring, first.Value), slots.Closed(ring, second.Value)));
        Assert.Null(Judge(proven, slots.ArrayOf(second.Value), slots.ArrayOf(first.Value)));
    }

    [Fact]
    public void ABoundOnOneSideLeavesTheOtherUnproven()
    {
        var apart = Knowing(Bound(), new Fact(new Term(n).Subtract(new Term(m)).Add(new Term(1))));

        var unproven = Judge(apart, slots.Closed(ring, first.Value), slots.Closed(ring, second.Value));

        Assert.Equal("fails when n <= m - 1", Case(unproven));
    }

    [Fact]
    public void AnUnknownSlotOfAClassIsProvenByTheReadThroughTheValue()
    {
        var local = new LocalVariable("ring", slots.Closed(ring, UnknownType.Instance));
        var held = new Value(local.Type, "ring", HandBuiltModel.Node());
        var read = new Value(slots.Int, "ring.Capacity", HandBuiltModel.Node());
        var state = Bound() with { Bindings = Bound().Bindings.Set(local, held).Set(new MemberHolder(held, capacity), read) };
        var proven = Knowing(state, Equal(new Term(read), new Term(n)));

        Assert.Null(SlotIdentity.Judge(proven, HandBuiltRefinements.Read(local), slots.Closed(ring, first.Value), reading));

        var unproven = SlotIdentity.Judge(state, HandBuiltRefinements.Read(local), slots.Closed(ring, first.Value), reading);

        Assert.Equal(("ring.Capacity", "n"), (unproven?.Left, unproven?.Right));
        Assert.Equal("fails when n <= ring.Capacity - 1", Case(unproven));
    }

    [Fact]
    public void ADependentParameterIsProvenByTheFactAboutIt()
    {
        var parameter = slots.Dependent("N");
        var buffer = HandBuiltSlots.Generic(HandBuiltModel.Class("Buffer<>"), "Size", slots.Int);
        var bound = new Value(slots.Int, "N", HandBuiltModel.Node());
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(parameter, bound) };
        var proven = Knowing(state, Equal(new Term(bound), new Term(5)));

        Assert.Null(Judge(proven, slots.Closed(buffer, parameter), slots.Closed(buffer, slots.Literal(5))));

        var unproven = Judge(state, slots.Closed(buffer, parameter), slots.Closed(buffer, slots.Literal(5)));

        Assert.Equal(("N", "5"), (unproven?.Left, unproven?.Right));
        Assert.Equal("fails when N <= 4", Case(unproven));
    }

    [Fact]
    public void ACaptureTheFactsKnowAsALiteralIsSpelledAsThatLiteral()
    {
        var seven = Knowing(Bound(), Equal(new Term(n), new Term(7)));

        Assert.Null(Judge(seven, slots.ArrayOf(first.Value), slots.ArrayOf(slots.Literal(7))));
        Assert.Null(Judge(seven, slots.ArrayOf(slots.Literal(7)), slots.ArrayOf(first.Value)));

        var unproven = Judge(seven, slots.ArrayOf(first.Value), slots.ArrayOf(second.Value));

        Assert.Equal(("7", "m"), (unproven?.Left, unproven?.Right));
        Assert.Equal("int[7]", DiagnosticSnapshot.Type(unproven!.Source).Spelling);
        Assert.Equal("int[m]", DiagnosticSnapshot.Type(unproven.Target).Spelling);
        Assert.Equal("fails when m >= 8", Case(unproven));
    }

    [Fact]
    public void TwoLiteralsOweNoProofSinceTheClassifierDecidesThem()
    {
        Assert.Null(Judge(FactState.Empty, slots.ArrayOf(slots.Literal(3)), slots.ArrayOf(slots.Literal(5))));
        Assert.False(SlotIdentity.Owes(slots.ArrayOf(slots.Literal(3)), slots.ArrayOf(slots.Literal(5))));
    }

    [Fact]
    public void ASlotTheTargetLeavesUnknownOwesNothing()
    {
        Assert.Null(Unflowed(slots.Closed(ring, first.Value), slots.Closed(ring, UnknownType.Instance)));
        Assert.Null(Unflowed(slots.ArrayOf(first.Value), slots.ArrayOf(new UnknownValueType(slots.Int))));
        Assert.Null(Unflowed(slots.Closed(ring, first.Value), slots.Int));
    }

    [Fact]
    public void AnUnknownSlotIsShownAsTheReadThroughTheValue()
    {
        var unproven = Unflowed(slots.ArrayOf(new UnknownValueType(slots.Int)), slots.ArrayOf(slots.Literal(3)));

        Assert.Equal(("ring.Length", "3"), (unproven?.Left, unproven?.Right));
    }

    [Fact]
    public void AClosingIsJudgedThroughTheBaseItReachesTheTargetBy()
    {
        var parameter = new TypeParameter("Capacity", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var wide = HandBuiltModel.Class("Wide<>", slots.Closed(ring, parameter));
        wide.TypeParametersList.Add(parameter);

        Assert.Null(Unflowed(slots.Closed(wide, first.Value), slots.Closed(ring, first.Value)));
        Assert.NotNull(Unflowed(slots.Closed(wide, first.Value), slots.Closed(ring, second.Value)));
        Assert.Null(Judge(Knowing(Bound(), Equal(new Term(n), new Term(m))), slots.Closed(wide, first.Value), slots.Closed(ring, second.Value)));
    }

    [Fact]
    public void ASlotThatFailedToResolveOwesNothing()
    {
        Assert.Null(Unflowed(slots.Closed(ring, first.Value), slots.Closed(ring, ErrorType.Instance)));
        Assert.Null(Unflowed(slots.Closed(ring, ErrorType.Instance), slots.Closed(ring, first.Value)));
    }

    [Fact]
    public void AnUnknownSlotIsOneValueWithACaptureTheFlowBoundToItsOwnRead()
    {
        var local = new LocalVariable("a", slots.ArrayOf(new UnknownValueType(slots.Int)));
        var other = new LocalVariable("other", slots.ArrayOf(new UnknownValueType(slots.Int)));
        var length = slots.Array.TypeParametersList[1];
        var read = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(local), "Length", slots.Int, member: new TypeResult(length));
        var inferred = new SlotCapture("a.Length", slots.Int, slots.Int, read, 7);
        var held = new Value(local.Type, "a", HandBuiltModel.Node());
        var measured = new Value(slots.Int, "a.Length", HandBuiltModel.Node());
        var unchanged = FactState.Empty with { Bindings = Bindings.Empty.Set(local, held).Set(new MemberHolder(held, length), measured).Set(inferred, measured) };
        var reassigned = unchanged with { Bindings = unchanged.Bindings.Set(local, new Value(local.Type, "a", HandBuiltModel.Node())) };
        var again = HandBuiltRefinements.Read(local, 3);

        Assert.Null(SlotIdentity.Judge(unchanged, again, slots.ArrayOf(inferred.Value), reading));
        Assert.Null(SlotIdentity.Judge(unchanged, new ImplicitConversionExpression(HandBuiltModel.Node(), again, again.Type, []), slots.ArrayOf(inferred.Value), reading));
        Assert.Equal(("a.Length", "a.Length"), SlotIdentity.Judge(reassigned, again, slots.ArrayOf(inferred.Value), reading) is { } unproven ? (unproven.Left, unproven.Right) : (null, null));
        Assert.Equal(("other.Length", "a.Length"), SlotIdentity.Judge(unchanged, HandBuiltRefinements.Read(other), slots.ArrayOf(inferred.Value), reading) is { } apart ? (apart.Left, apart.Right) : (null, null));
        Assert.NotNull(SlotIdentity.Judge(FactState.Empty, again, slots.ArrayOf(inferred.Value), reading));
    }

    [Fact]
    public void AnOptionalTargetIsJudgedByTheClosingItHolds()
    {
        var optional = new UnionType([slots.Closed(ring, second.Value), slots.Refinements.Built.Type("Void")]);

        Assert.NotNull(Unflowed(slots.Closed(ring, first.Value), optional));
        Assert.Null(Unflowed(slots.Closed(ring, second.Value), optional));
        Assert.Null(Judge(Knowing(Bound(), Equal(new Term(n), new Term(m))), slots.Closed(ring, first.Value), optional));
    }

    [Fact]
    public void AClosingOwesAProofOnlyWhereASlotPairIsNotOneObject()
    {
        Assert.True(SlotIdentity.Owes(slots.Closed(ring, first.Value), slots.Closed(ring, second.Value)));
        Assert.True(SlotIdentity.Owes(slots.Closed(ring, UnknownType.Instance), slots.Closed(ring, slots.Literal(3))));
        Assert.False(SlotIdentity.Owes(slots.Closed(ring, first.Value), slots.Closed(ring, first.Value)));
        Assert.False(SlotIdentity.Owes(slots.Closed(ring, first.Value), slots.Closed(ring, UnknownType.Instance)));
        Assert.False(SlotIdentity.Owes(slots.Int, slots.Closed(ring, first.Value)));
    }

    [Fact]
    public void ANestedUnknownSlotIsNotReadThroughTheOuterValue()
    {
        var box = HandBuiltModel.Class("Box<>");
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        var local = new LocalVariable("b", slots.Closed(box, slots.Closed(ring, UnknownType.Instance)));
        var held = new Value(local.Type, "b", HandBuiltModel.Node());
        var read = new Value(slots.Int, "b.Capacity", HandBuiltModel.Node());
        var state = Bound() with { Bindings = Bound().Bindings.Set(local, held).Set(new MemberHolder(held, capacity), read) };
        var claimed = Knowing(state, Equal(new Term(read), new Term(n)));

        var unproven = SlotIdentity.Judge(claimed, HandBuiltRefinements.Read(local), slots.Closed(box, slots.Closed(ring, first.Value)), reading);

        Assert.Equal(("unknown", "n"), (unproven?.Left, unproven?.Right));
        Assert.Null(unproven?.Proof);
        Assert.Null(SlotIdentity.Judge(claimed, HandBuiltRefinements.Read(local), slots.Closed(box, slots.Closed(ring, UnknownType.Instance)), reading));

        var seven = SlotIdentity.Judge(Knowing(claimed, Equal(new Term(n), new Term(7))), HandBuiltRefinements.Read(local), slots.Closed(box, slots.Closed(ring, first.Value)), reading);

        Assert.Equal(("unknown", "7"), (seven?.Left, seven?.Right));
        Assert.Equal("Box<Ring<7>>", DiagnosticSnapshot.Type(seven!.Target).Spelling);
        Assert.Null(SlotIdentity.Judge(claimed, HandBuiltRefinements.Local(slots.Closed(box, slots.Closed(ring, first.Value)), "c"), slots.Closed(box, slots.Closed(ring, first.Value)), reading));
    }
}