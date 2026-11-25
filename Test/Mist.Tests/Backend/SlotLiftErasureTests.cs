using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class SlotLiftErasureTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void AParameterizedSlot_IsSpelledAsItsAbstractBound_ClosedByTheUseSitesArgument()
    {
        var sequence = Generic("ISeq<>", "T");
        var functor = Abstract("IFunctor");
        var mapped = Slot(functor, "Mapped<>", "TItem");
        mapped.ConstraintsList.Add(new TypeConstraint(new ConstructedType(sequence, [mapped.TypeParametersList[0]])));

        Assert.Equal("Probe.ISeq<int>", TypeNameEmitter.Emit(new ConstructedType(mapped, [built.Type("Int32")])));
    }

    [Fact]
    public void ErasureSubstitutesEachSlotParameterByPosition_NotByTheBoundsArgumentOrder()
    {
        var pair = Generic("IPair<>", "TFirst", "TSecond");
        var owner = Abstract("IPairs");
        var swapped = Slot(owner, "Swapped<>", "A", "B");
        swapped.ConstraintsList.Add(new TypeConstraint(
            new ConstructedType(pair, [swapped.TypeParametersList[1], swapped.TypeParametersList[0]])));

        Assert.Equal(
            "Probe.IPair<string, int>",
            TypeNameEmitter.Emit(new ConstructedType(swapped, [built.Type("Int32"), built.Type("String")])));
    }

    [Fact]
    public void ANonAbstractTypeConstraint_IsNotABound_SoErasureTakesTheAbstractOneAfterIt()
    {
        var shown = Abstract("IShow");
        var owner = Abstract("IHolder");
        var held = Slot(owner, "Held<>", "T");
        held.ConstraintsList.Add(new TypeConstraint(HandBuiltModel.Class("Item")));
        held.ConstraintsList.Add(new TypeConstraint(shown));
        var classOnly = Slot(owner, "Boxed<>", "T");
        classOnly.ConstraintsList.Add(new TypeConstraint(HandBuiltModel.Class("Item")));

        Assert.Same(shown, SlotLift.Erased(new ConstructedType(held, [built.Type("Int32")])));
        Assert.Null(SlotLift.Erased(new ConstructedType(classOnly, [built.Type("Int32")])));
    }

    [Fact]
    public void EveryParameterizedSlotWithoutAnAbstractBound_IsRefusedOnce_AtTheDeclaration_ByItsBareName()
    {
        var declaration = new BodyTypeTypeDeclarationNode
        {
            Span = new Storm.Runtime.SourceSpan(7, 5),
            Origin = new ParsedOrigin(new SourceText("store.mist", new string(' ', 20))),
        };
        var store = new AbstractType("IStore", "Probe", new Scope(null), null, declaration);
        _ = Slot(store, "Element");
        Slot(store, "Mapped<>", "T").ConstraintsList.Add(new TypeConstraint(Abstract("IShow")));
        _ = Slot(store, "Raw<>", "T");
        Slot(store, "Boxed<>", "T").ConstraintsList.Add(new TypeConstraint(HandBuiltModel.Class("Item")));
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        SlotLift.ReportUnboundedParameterized(store, diagnostics);

        var refusals = diagnostics.Admitted;
        Assert.All(refusals, refusal =>
        {
            Assert.Equal(BackendDiagnostics.UnboundedParameterizedTypeSlot.Number, refusal.Number);
            Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
            var range = Assert.IsType<SourceRange>(refusal.Location);
            Assert.Equal((7, 5), (range.Offset, range.Length));
        });
        Assert.Equal(["Raw", "Boxed"], refusals.Select(refusal => Assert.IsType<NameValue>(refusal.Arguments[1]).Name));
        Assert.Contains("'IStore.Raw'", refusals[0].Message);
    }

    [Fact]
    public void AnUnboundedParameterizedSlotOnATypeWithNoDeclaration_IsRefusedAtItsAssemblyAndQualifiedName()
    {
        var store = new AbstractType("IStore", "Probe", new Scope(null), null, null)
        {
            Assembly = new MistAssembly("Library", MistAssemblyKind.Reference),
        };
        _ = Slot(store, "Raw<>", "T");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        SlotLift.ReportUnboundedParameterized(store, diagnostics);

        var refusal = Assert.Single(diagnostics.Admitted);
        Assert.Equal(BackendDiagnostics.UnboundedParameterizedTypeSlot.Number, refusal.Number);
        Assert.Equal(new ExternalLocation("Library", "Probe.IStore"), refusal.Location);
    }

    [Fact]
    public void AConstraintOnAConstructedAbstract_KeepsItsWrittenArgumentsAheadOfTheLiftedSlot()
    {
        var keyed = KeyedBag();

        Assert.Equal(
            "Probe.IKeyedBag<int, Element>",
            TypeNameEmitter.Emit(SlotLift.Constraint(new ConstructedType(keyed, [built.Type("Int32")]))));
    }

    [Fact]
    public void AConformerOfAConstructedAbstract_PassesItsFillerAfterTheWrittenArguments()
    {
        var keyed = KeyedBag();
        var strings = HandBuiltModel.Class("Strings");
        strings.NestedTypes.Add(new TypeAlias("Element", "Probe", new Scope(null), strings, null!) { AliasedType = built.Type("String") });

        Assert.Equal(
            "Probe.IKeyedBag<int, string>",
            TypeNameEmitter.Emit(SlotLift.Base(new ConstructedType(keyed, [built.Type("Int32")]), strings)!));
    }

    private static AbstractType KeyedBag()
    {
        var keyed = Generic("IKeyedBag<>", "K");
        _ = Slot(keyed, "Element");
        return keyed;
    }

    private static AbstractType Abstract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }

    private static AbstractType Generic(string name, params string[] parameters)
    {
        var type = Abstract(name);
        type.TypeParametersList.AddRange(parameters.Select((parameter, ordinal) => new TypeParameter(parameter, ordinal, type.Scope, type)));
        return type;
    }

    private static TypeSlot Slot(AbstractType owner, string name, params string[] parameters)
    {
        var slot = new TypeSlot(name, owner.TypeSlotsList.Count, owner.Scope, owner);
        slot.TypeParametersList.AddRange(parameters.Select((parameter, ordinal) => new TypeParameter(parameter, ordinal, owner.Scope, slot)));
        owner.TypeSlotsList.Add(slot);
        return slot;
    }
}