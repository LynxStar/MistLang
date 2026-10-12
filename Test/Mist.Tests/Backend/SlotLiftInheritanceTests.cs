using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 1: SlotLift over hand-built abstracts inheriting `type Element;` -- which slots a derived abstract
// lifts, in what order, and what its base list, a conformer's base list and a constrained method pass
// for them.
[Trait("Tier", "1")]
public sealed class SlotLiftInheritanceTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void ADerivedAbstract_LiftsTheSlotItInherits_AndPassesItToItsBase()
    {
        var bag = Abstract("IBag", "Element");
        var named = Abstract("INamedBag", null, bag);

        Assert.Same(bag.TypeSlotsList[0], Assert.Single(SlotLift.Lifted(named)));
        Assert.Equal("INamedBag<Element>", TypeEmitter.EmitTypeName(named));
        Assert.Equal("Probe.IBag<Element>", TypeNameEmitter.Emit(SlotLift.Base(bag, named)!));
    }

    [Fact]
    public void AConformerOfADerivedAbstract_PassesItsFillerForTheInheritedSlot()
    {
        var bag = Abstract("IBag", "Element");
        var named = Abstract("INamedBag", null, bag);
        var strings = Conformer(named);

        Assert.Equal("Probe.INamedBag<string>", TypeNameEmitter.Emit(SlotLift.Base(named, strings)!));
    }

    [Fact]
    public void AGenericDerivedAbstract_KeepsItsWrittenParametersFirst()
    {
        var bag = Abstract("IBag", "Element");
        var keyed = Abstract("IKeyedBag<>", null, bag);
        keyed.TypeParametersList.Add(new TypeParameter("K", 0, keyed.Scope, keyed));

        Assert.Equal("IKeyedBag<K, Element>", TypeEmitter.EmitTypeName(keyed));
        Assert.Equal("Probe.IBag<Element>", TypeNameEmitter.Emit(SlotLift.Base(bag, keyed)!));
    }

    [Fact]
    public void OwnSlotsComeFirst_ThenInheritedOnesInReachOrder_ThroughEveryLevel()
    {
        var first = Abstract("IFirst", "F");
        var deeper = Abstract("IDeeper", "D");
        var second = Abstract("ISecond", "S", deeper);
        var own = Abstract("IOwn", "O", first, second);

        Assert.Equal(["O", "F", "S", "D"], SlotLift.Lifted(own).Select(slot => slot.Name));
        Assert.Equal("Probe.ISecond<S, D>", TypeNameEmitter.Emit(SlotLift.Base(second, own)!));
    }

    [Fact]
    public void ADiamond_LiftsTheSharedSlotOnce()
    {
        var a = Abstract("IA", "E");
        var b = Abstract("IB", null, a);
        var c = Abstract("IC", null, a);
        var d = Abstract("ID", null, b, c);

        Assert.Same(a.TypeSlotsList[0], Assert.Single(SlotLift.Lifted(d)));
        Assert.Equal("Probe.IC<E>", TypeNameEmitter.Emit(SlotLift.Base(c, d)!));
    }

    [Fact]
    public void CoincidingSlotNames_ShareOneParameter_AndAnOwnSlotShadowsAnInheritedOne()
    {
        var a = Abstract("IA", "E");
        var x = Abstract("IX", "E");
        var both = Abstract("IBoth", null, a, x);
        var shadowing = Abstract("IShadow", "E", a);

        Assert.Same(a.TypeSlotsList[0], Assert.Single(SlotLift.Lifted(both)));
        Assert.Equal("Probe.IX<E>", TypeNameEmitter.Emit(SlotLift.Base(x, both)!));
        Assert.Same(shadowing.TypeSlotsList[0], Assert.Single(SlotLift.Lifted(shadowing)));
        Assert.Equal("Probe.IA<E>", TypeNameEmitter.Emit(SlotLift.Base(a, shadowing)!));
    }

    [Fact]
    public void AnInheritedSlotsBound_IsRestatedOnTheDerivedInterface()
    {
        var shown = Abstract("IShow");
        var producer = Abstract("IProducer", "Output");
        producer.TypeSlotsList[0].ConstraintsList.Add(new TypeConstraint(shown));
        var named = Abstract("INamedProducer", null, producer);

        Assert.Equal(["where Output : IShow"], ConstraintEmitter.Lines(TypeEmitter.Parameters(named), liftSlots: false));
    }

    [Fact]
    public void AMethodConstrainedToADerivedAbstract_SynthesizesTheInheritedSlot_AndACallSuppliesTheFiller()
    {
        var bag = Abstract("IBag", "Element");
        var named = Abstract("INamedBag", null, bag);
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        parameter.ConstraintsList.Add(new TypeConstraint(named));
        var strings = Conformer(named);

        Assert.Equal("Probe.INamedBag<Element>", TypeNameEmitter.Emit(SlotLift.Constraint(named)));
        Assert.Same(bag.TypeSlotsList[0], Assert.Single(SlotLift.Synthesized([parameter])));
        Assert.Same(built.Type("String"), Assert.Single(SlotLift.Fillers([strings], [parameter])));
    }

    [Fact]
    public void TheSlotOfAClrProjectedBase_IsNotLiftedOntoTheDeriver()
    {
        var enumerable = new AbstractType("IEnumerable<>", "Standard", new Scope(null), null, null);
        enumerable.TypeParametersList.Add(new TypeParameter("TElement", 0, enumerable.Scope, enumerable));
        enumerable.TypeSlotsList.Add(new TypeSlot("Enumerator", 0, enumerable.Scope, enumerable));
        var sequence = Abstract("ISequence", null, new ConstructedType(enumerable, [built.Type("Int32")]));

        Assert.Empty(SlotLift.Lifted(sequence));
    }

    private static AbstractType Abstract(string name, string? slot = null, params MistType[] bases)
    {
        var type = new AbstractType(name, "Probe", new Scope(null), null, null);

        if(slot is not null)
        {
            type.TypeSlotsList.Add(new TypeSlot(slot, 0, type.Scope, type));
        }

        type.BaseAbstractTypes.AddRange(bases);
        return type;
    }

    private ClassType Conformer(AbstractType abstractType)
    {
        var conformer = HandBuiltModel.Class("Strings");
        conformer.BaseAbstractTypes.Add(abstractType);
        conformer.NestedTypes.Add(new TypeAlias("Element", "Probe", new Scope(null), conformer, null!) { AliasedType = built.Type("String") });
        return conformer;
    }
}