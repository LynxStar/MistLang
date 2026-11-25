using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class SlotLiftFillerTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void AConstructedArgument_IsFilledFromTheNestedTypeOnItsDefinition()
    {
        var bag = Abstract("IBag", "Element");
        var box = HandBuiltModel.Class("Box<>");
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        box.NestedTypes.Add(Alias(box, "Element", built.Type("String")));

        Assert.Same(built.Type("String"), Assert.Single(
            SlotLift.Fillers([new ConstructedType(box, [built.Type("Int32")])], [Constrained("T", 0, bag)])));
    }

    [Fact]
    public void AnArgumentWithNoFiller_PassesTheSlotItself_WhetherATypeParameter_NoMemberType_UnfilledOrMissing()
    {
        var a = Abstract("IA", "A");
        var b = Abstract("IB", "B");
        var c = Abstract("IC", "C");
        var d = Abstract("ID", "D");
        var callerParameter = new TypeParameter("U", 0, new Scope(null), null);
        var tuple = new TupleType([built.Type("Int32"), built.Type("String")]);
        var unfilled = HandBuiltModel.Class("Empty");

        var fillers = SlotLift.Fillers(
            [callerParameter, tuple, unfilled],
            [Constrained("T0", 0, a), Constrained("T1", 1, b), Constrained("T2", 2, c), Constrained("T3", 3, d)]);

        Assert.Collection(
            fillers,
            filler => Assert.Same(a.TypeSlotsList[0], filler),
            filler => Assert.Same(b.TypeSlotsList[0], filler),
            filler => Assert.Same(c.TypeSlotsList[0], filler),
            filler => Assert.Same(d.TypeSlotsList[0], filler));
    }

    [Fact]
    public void ASlotReachedFromSeveralParametersAndConstraints_IsSynthesizedOnce_AndFilledByTheFirstParameterReachingIt()
    {
        var shown = Abstract("IShow");
        var bag = Abstract("IBag", "Element");
        var named = Abstract("INamedBag", null, bag);
        var first = Constrained("T0", 0, shown);
        var second = Constrained("T1", 1, bag, named);
        var third = Constrained("T2", 2, named);

        Assert.Same(bag.TypeSlotsList[0], Assert.Single(SlotLift.Synthesized([first, second, third])));
        Assert.Same(built.Type("String"), Assert.Single(SlotLift.Fillers(
            [Filling("Shows", built.Type("Char")), Filling("Strings", built.Type("String")), Filling("Ints", built.Type("Int32"))],
            [first, second, third])));
    }

    [Fact]
    public void ABaseWithAnyUnfilledSlot_IsLeftAsWritten_RatherThanPartiallyFilled()
    {
        var pair = Abstract("IPair", "Key", null, "Value");
        var keyOnly = Filling("KeyOnly", built.Type("Int32"), "Key");
        var both = Filling("Both", built.Type("Int32"), "Key");
        both.NestedTypes.Add(Alias(both, "Value", built.Type("String")));

        Assert.Null(SlotLift.Base(pair, keyOnly));
        Assert.Equal("Probe.IPair<int, string>", TypeNameEmitter.Emit(SlotLift.Base(pair, both)!));
    }

    private static ClassType Filling(string name, IMistType filler, string slot = "Element")
    {
        var conformer = HandBuiltModel.Class(name);
        conformer.NestedTypes.Add(Alias(conformer, slot, filler));
        return conformer;
    }

    private static TypeAlias Alias(MemberType owner, string name, IMistType aliased)
    {
        return new TypeAlias(name, "Probe", new Scope(null), owner, null!) { AliasedType = aliased };
    }

    private static TypeParameter Constrained(string name, int ordinal, params AbstractType[] constraints)
    {
        var parameter = new TypeParameter(name, ordinal, new Scope(null), null);
        parameter.ConstraintsList.AddRange(constraints.Select(constraint => new TypeConstraint(constraint)));
        return parameter;
    }

    private static AbstractType Abstract(string name, string? slot = null, AbstractType? inherited = null, string? secondSlot = null)
    {
        var type = new AbstractType(name, "Probe", new Scope(null), null, null);

        foreach(var slotName in new[] { slot, secondSlot }.OfType<string>())
        {
            type.TypeSlotsList.Add(new TypeSlot(slotName, type.TypeSlotsList.Count, type.Scope, type));
        }

        if(inherited is not null)
        {
            type.BaseAbstractTypes.Add(inherited);
        }

        return type;
    }
}