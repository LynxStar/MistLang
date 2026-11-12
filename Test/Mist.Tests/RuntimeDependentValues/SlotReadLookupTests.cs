using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// The member lookup decision for a dependent parameter under a qualified instance request, and the one
/// read it refuses: the slot of an abstract the receiver's type leaves unknown.
/// </summary>
[Trait("Tier", "1")]
public sealed class SlotReadLookupTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly AbstractType sized;

    public SlotReadLookupTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        sized = (AbstractType)HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", new Scope(null), null, null), "N", slots.Int);
    }

    private static LookupResult Instance(MistType type, string name)
    {
        return TypeMemberLookup.Find(type, LookupRequest.Member(name, MemberVisibility.InstanceOnly, null));
    }

    [Fact]
    public void AClassAndAStructAnswerTheirDependentParameterThroughAnInstance()
    {
        var cell = HandBuiltSlots.Generic(HandBuiltModel.Struct("Cell<>"), "Size", slots.Int);

        var capacity = Assert.IsType<TypeResult>(Instance(ring, "Capacity"));
        var size = Assert.IsType<TypeResult>(Instance(cell, "Size"));

        Assert.Same(ring.TypeParametersList[0], capacity.Type);
        Assert.Empty(capacity.BaseConstructions ?? []);
        Assert.Same(cell.TypeParametersList[0], size.Type);
    }

    [Fact]
    public void ADerivingTypeReadsTheBasesParameterThroughTheConstructionItNamed()
    {
        var nine = slots.Closed(ring, slots.Literal(9));
        var fixedRing = HandBuiltModel.Class("Fixed", nine);

        var found = Assert.IsType<TypeResult>(Instance(fixedRing, "Capacity"));

        Assert.Same(ring.TypeParametersList[0], found.Type);
        Assert.Same(nine, Assert.Single(found.BaseConstructions!));
        Assert.Same(nine, InstanceSlotRead.Closing(found, null, slots.Refinements.Built.Interner));
    }

    [Fact]
    public void ANamedClosingOfAnAbstractIsFoundAndHasAnAnswer()
    {
        var impl = HandBuiltModel.Class("Impl");
        var three = slots.Closed(sized, slots.Literal(3));
        impl.BaseAbstractTypes.Add(three);

        var found = Assert.IsType<TypeResult>(Instance(impl, "N"));
        var slot = Assert.IsType<TypeParameter>(found.Type);

        Assert.Same(sized.TypeParametersList[0], slot);
        Assert.Null(InstanceSlotRead.ThroughUnknown(slot, InstanceSlotRead.Closing(found, null, slots.Refinements.Built.Interner)));
    }

    [Fact]
    public void AnAbstractClosedWithUnknownIsFoundAndRefused()
    {
        var some = slots.Closed(sized, new UnknownValueType(slots.Int));

        var found = Assert.IsType<TypeResult>(Instance(sized, "N"));
        var slot = Assert.IsType<TypeParameter>(found.Type);
        var closing = InstanceSlotRead.Closing(found, some, slots.Refinements.Built.Interner);

        Assert.Same(some, InstanceSlotRead.ThroughUnknown(slot, closing));
    }

    [Fact]
    public void AClassClosedWithUnknownStillAnswers()
    {
        var some = slots.Closed(ring, new UnknownValueType(slots.Int));

        var found = Assert.IsType<TypeResult>(Instance(ring, "Capacity"));
        var slot = Assert.IsType<TypeParameter>(found.Type);

        Assert.Null(InstanceSlotRead.ThroughUnknown(slot, InstanceSlotRead.Closing(found, some, slots.Refinements.Built.Interner)));
    }

    [Fact]
    public void TheReadIsTypedByTheSlotsDomainWhateverTheClosingHoldsAndWhateverItsWhereSays()
    {
        var capacity = ring.TypeParametersList[0];
        var captured = slots.Capture(new LocalVariable("n", slots.Int)).Value;
        var bounded = HandBuiltSlots.Generic(HandBuiltModel.Class("Bounded<>"), "Capacity", slots.Int);
        var limit = bounded.TypeParametersList[0];
        limit.RefinementNode = HandBuiltModel.Node();
        limit.Refinement = new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(slots.Int, 1L));

        _ = Assert.IsType<RefinedType>(InstanceSlotRead.Contract(limit));
        Assert.Same(slots.Int, InstanceSlotRead.TypeOf(limit, slots.Closed(bounded, slots.Literal(4))));
        Assert.Same(slots.Int, InstanceSlotRead.TypeOf(limit, slots.Closed(bounded, new UnknownValueType(slots.Int))));

        Assert.Same(slots.Int, InstanceSlotRead.TypeOf(capacity, slots.Closed(ring, slots.Literal(4))));
        Assert.Same(slots.Int, InstanceSlotRead.TypeOf(capacity, slots.Closed(ring, captured)));
        Assert.Same(slots.Int, InstanceSlotRead.TypeOf(capacity, slots.Closed(ring, new UnknownValueType(slots.Int))));
        Assert.Same(ErrorType.Instance, InstanceSlotRead.TypeOf(capacity, slots.Closed(ring, ErrorType.Instance)));
        Assert.Null(InstanceSlotRead.TypeOf(capacity, null));
    }

    [Fact]
    public void AnOrdinaryParameterAndAStaticRequestAreUnchanged()
    {
        var holder = HandBuiltModel.Class("Holder<>");
        holder.TypeParametersList.Add(new TypeParameter("T", 0, holder.Scope, holder));

        _ = Assert.IsType<NotFoundResult>(Instance(holder, "T"));
        Assert.Null(InstanceSlotRead.Parameter(Instance(holder, "T")));
        _ = Assert.IsType<NotFoundResult>(TypeMemberLookup.Find(ring, LookupRequest.Member("Capacity", MemberVisibility.StaticOnly, null)));
    }
}