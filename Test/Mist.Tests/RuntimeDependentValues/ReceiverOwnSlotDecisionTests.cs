using System.Collections.Immutable;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class ReceiverOwnSlotDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly FactReading reading = new(new LiftedReturns());
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly SlotCapture first;
    private readonly Value n;
    private readonly Value m;

    public ReceiverOwnSlotDecisionTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.ZeroOrPositive);
        capacity = ring.TypeParametersList[0];
        first = slots.Capture(new LocalVariable("n", slots.Int));
        n = new Value(slots.Int, "n", HandBuiltModel.Node());
        m = new Value(slots.Int, "m", HandBuiltModel.Node());
    }

    private IMistType Unknown => slots.Refinements.Built.Interner.GetOrCreateUnknownValue(slots.Int);

    private IMistType Optional(IMistType held)
    {
        return slots.Refinements.Built.Interner.GetOrCreateUnionType([held, slots.Refinements.Built.Type("Void")]);
    }

    private static FactState Evaluated(FactState state, Expression receiver, Value held)
    {
        return state with { Results = ImmutableDictionary<object, Term>.Empty.SetItem(receiver, new Term(held)) };
    }

    [Fact]
    public void TheReceiversOwnSlotIsReadFromTheValueTheReceiverHeldWhenItWasEvaluated()
    {
        var u = new LocalVariable("u", slots.Closed(ring, UnknownType.Instance));
        var receiver = HandBuiltRefinements.Read(u);
        var before = new Value(u.Type, "u", HandBuiltModel.Node());
        var after = new Value(u.Type, "u", HandBuiltModel.Node());
        var state = FactState.Empty with
        {
            Bindings = Bindings.Empty.Set(first, n).Set(u, after).Set(new MemberHolder(before, capacity), m).Set(new MemberHolder(after, capacity), n),
        };
        var meeting = new SlotMeeting([first], Receiver: receiver) { Declared = slots.ArrayOf(capacity) };

        var unproven = SlotIdentity.Judge(Evaluated(state, receiver, before), slots.Create(first), slots.ArrayOf(Unknown), reading, meeting: meeting);

        Assert.Equal(("n", "u.Capacity"), (unproven?.Left, unproven?.Right));
        Assert.Null(SlotIdentity.Judge(Evaluated(state, receiver, after), slots.Create(first), slots.ArrayOf(Unknown), reading, meeting: meeting));
    }

    [Fact]
    public void AnArgumentReachedThroughAMemberHasTheSlotOfThatMembersOwnReceiver()
    {
        var x = new LocalVariable("x", slots.Closed(ring, first.Value));
        var receiver = HandBuiltRefinements.Read(x);
        var last = new Field("Last", slots.ArrayOf(capacity), OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = ring };
        var read = new MemberAccessExpression(HandBuiltModel.Node(), receiver, last.Name, slots.ArrayOf(first.Value), member: new FieldResult(last, ring, []));
        var own = slots.Capture(new LocalVariable("u", slots.Int));
        var held = new Value(x.Type, "x", HandBuiltModel.Node());
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(x, held).Set(new MemberHolder(held, capacity), m).Set(own, m) };

        Assert.Equal<(Expression, TypeParameter)?>((receiver, capacity), OwnSlots.Slot(read, 1));
        Assert.Null(SlotIdentity.Judge(Evaluated(state, receiver, held), read, slots.ArrayOf(own.Value), reading, meeting: new SlotMeeting([own])));
        Assert.NotNull(SlotIdentity.Judge(Evaluated(state with { Bindings = state.Bindings.Set(own, n) }, receiver, held), read, slots.ArrayOf(own.Value), reading, meeting: new SlotMeeting([own])));
    }

    [Fact]
    public void AReceiverReachedByOptionalChainingMeetsItsOwnSlotAsAPlainReceiverDoes()
    {
        var known = HandBuiltRefinements.Local(Optional(slots.Closed(ring, first.Value)), "u");
        var unknown = HandBuiltRefinements.Local(Optional(slots.Closed(ring, UnknownType.Instance)), "u");
        var owned = new Dictionary<SlotCapture, TypeParameter>(ReferenceEqualityComparer.Instance);

        Assert.Same(capacity, new SlotMeeting([], Receiver: known).ReceiverSlot(first.Value));
        _ = Assert.IsType<CapturedValue>(Assert.IsType<ConstructedType>(OwnSlots.Mark(unknown, slots.ArrayOf(capacity), slots.ArrayOf(Unknown), owned)).TypeArguments[1]);
        Assert.Same(capacity, Assert.Single(owned).Value);
    }

    [Fact]
    public void AnExtensionBlocksSlotIsTheReceiversOwnWhereTheBlocksReceiverTypeClosesWithIt()
    {
        var scope = new ExtensionScope(null);
        var block = new ExtensionBlock("self", "", scope, null);
        var c = new TypeParameter("C", 0, scope, null) { IsDependent = true, Domain = (MistType)slots.Int };
        block.TypeParametersList.Add(c);
        block.ResolvedReceiverType = slots.Closed(ring, c);
        var owned = new Dictionary<SlotCapture, TypeParameter>(ReferenceEqualityComparer.Instance);

        _ = OwnSlots.Mark(HandBuiltRefinements.Local(slots.Closed(ring, UnknownType.Instance), "u"), slots.ArrayOf(c), slots.ArrayOf(Unknown), owned);

        Assert.Same(capacity, Assert.Single(owned).Value);
    }

    [Fact]
    public void AValueReachedThroughAnOptionalMemberTypedWithItsOwnersSlotHoldsTheOwnersSlot()
    {
        var holder = HandBuiltSlots.Generic(HandBuiltModel.Class("Hd<>"), "K", slots.ZeroOrPositive);
        var k = holder.TypeParametersList[0];
        var member = new Property("R", Optional(slots.Closed(ring, k)), new MemberModifiers()) { DeclaringType = holder };
        var h = new LocalVariable("h", slots.Closed(holder, UnknownType.Instance));
        var receiver = HandBuiltRefinements.Read(h);
        var reached = new MemberAccessExpression(HandBuiltModel.Node(), receiver, member.Name, Optional(slots.Closed(ring, UnknownType.Instance)), member: new PropertyResult(member, holder, []));
        var owner = new Value(h.Type, "h", HandBuiltModel.Node());
        var made = new Value(reached.Type, "h.R", HandBuiltModel.Node());
        var state = FactState.Empty with { Bindings = Bindings.Empty.Set(new MemberHolder(owner, k), m) };

        Assert.Same(m, SlotReads.Created(Evaluated(state, receiver, owner), reached, new Term(made)).Bindings.Get(new MemberHolder(made, capacity)));
        Assert.Null(SlotReads.Created(state, reached, new Term(made)).Bindings.Get(new MemberHolder(made, capacity)));
    }

    [Fact]
    public void AnOptionalMemberTypedWithItsOwnersSlotIsMarkedWithThatSlotThroughTheOptional()
    {
        var holder = HandBuiltSlots.Generic(HandBuiltModel.Class("Hd<>"), "K", slots.ZeroOrPositive);
        var k = holder.TypeParametersList[0];
        var h = HandBuiltRefinements.Local(slots.Closed(holder, UnknownType.Instance), "h");
        var owned = new Dictionary<SlotCapture, TypeParameter>(ReferenceEqualityComparer.Instance);

        var marked = Assert.IsType<UnionType>(OwnSlots.Mark(h, Optional(slots.Closed(ring, k)), Optional(slots.Closed(ring, UnknownType.Instance)), owned));

        _ = Assert.IsType<CapturedValue>(Assert.Single(marked.Constituents.OfType<ConstructedType>()).TypeArguments[0]);
        Assert.Same(k, Assert.Single(owned).Value);
    }

    [Fact]
    public void AnOptionalFitsAnOptionalThroughTheSlotsOfTheValueItHolds()
    {
        var second = slots.Capture(new LocalVariable("m", slots.Int));

        Assert.NotNull(SlotIdentity.Judge(FactState.Empty, Optional(slots.Closed(ring, first.Value)), Optional(slots.Closed(ring, second.Value)), through: null));
        Assert.Null(SlotIdentity.Judge(FactState.Empty, Optional(slots.Closed(ring, first.Value)), Optional(slots.Closed(ring, first.Value)), through: null));
    }
    [Fact]
    public void AbsenceFitsAnOptionalWhateverSlotItsContextualTypeNames()
    {
        var target = Optional(slots.Closed(ring, slots.Capture(new LocalVariable("m", slots.Int)).Value));
        var contextual = Optional(slots.Closed(ring, first.Value));

        Assert.Null(SlotIdentity.Judge(FactState.Empty, new VoidLiteralExpression(HandBuiltModel.Node(), VoidReading.Absence, contextual), target));
        Assert.Null(SlotIdentity.Judge(FactState.Empty, new DefaultExpression(HandBuiltModel.Node(), contextual), target));
        Assert.NotNull(SlotIdentity.Judge(FactState.Empty, new DefaultExpression(HandBuiltModel.Node(), slots.Closed(ring, first.Value)), target));
    }
}