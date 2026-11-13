using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// What the backend's other whole-declaration rewrites make of a value-routed slot (runtime-dependent-values
/// design, <em>The value route</em>): a union over the slot alone is one C# spelling and specializes nothing;
/// a declaration specialized for a carried parameter keeps the slot open in each copy, keyed and named
/// without it; a copy of any declaration with a closing of the type standing in a type argument, the type's own
/// members included, carries no value of the slot and refuses each read of it, while the slot normalized into the
/// copy's own ordinal stays in reach; a union carrier names a closing of the type by the slot's domain whatever stands in the
/// slot, and refuses two closings of one such type; a variadic reference leaves the slot out of its arguments.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteSpecializationTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly IMistType text;

    public ValueRouteSpecializationTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        ring.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
        text = slots.Refinements.Built.Type("String");
        _ = slots.Refinements.Built.Model.RegisterType(ring);
    }

    [Fact]
    public void AUnionOverAValueRoutedSlotAloneIsOneSpellingAndSpecializesNothing()
    {
        var model = slots.Refinements.Built.Model;
        var union = Union(slots.Closed(ring, capacity), text);
        var closing = slots.Closed(ring, slots.Capture(new LocalVariable("n", slots.Int)).Value);
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        Assert.False(UnionSpecialization.Specializes(ring));
        Assert.Null(UnionSpecialization.NameOf(closing));
        Assert.False(CSharpEmitterV2.IsOpen(union));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AUnionOverAValueRoutedSlotStillSpecializesWhileTheSlotIsCarried()
    {
        var model = slots.Refinements.Built.Model;
        var union = Union(slots.Closed(ring, capacity), text);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        Assert.True(UnionSpecialization.Specializes(ring));
        Assert.True(CSharpEmitterV2.IsOpen(union));
    }

    [Fact]
    public void ADeclarationSpecializedForACarriedParameterKeepsItsValueRoutedSlotOpenInEveryCopy()
    {
        var model = slots.Refinements.Built.Model;
        var (pair, item, n) = Pair();
        _ = Union(item, text);
        var first = slots.Closed(pair, text, slots.Capture(new LocalVariable("n", slots.Int)).Value);
        var second = slots.Closed(pair, text, slots.Literal(5));
        var third = slots.Closed(pair, slots.Refinements.Bool, slots.Capture(new LocalVariable("m", slots.Int)).Value);
        using var routes = SlotRoutes.Enter([n]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        var instances = UnionSpecialization.InstancesOf(pair);

        Assert.Equal(2, instances.Count);
        Assert.All(instances, instance => Assert.Same(n, instance.TypeArguments[1]));
        Assert.Equal(UnionSpecialization.NameOf(first), UnionSpecialization.NameOf(second));
        Assert.NotEqual(UnionSpecialization.NameOf(first), UnionSpecialization.NameOf(third));
        Assert.Equal(UnionSpecialization.Name("Pair", [text, n]), UnionSpecialization.NameOf(first));

        var lines = Body(pair, instances.Single(instance => ReferenceEquals(instance.TypeArguments[0], text)));

        Assert.Contains($"public class {UnionSpecialization.NameOf(first)}", lines);
        Assert.Contains("public readonly int __N;", lines);
        Assert.Contains($"public {UnionSpecialization.NameOf(first)}(int __N)", lines);
    }

    [Fact]
    public void AMethodSpecializedForACarriedParameterIsNamedWithoutItsValueRoutedSlot()
    {
        var model = slots.Refinements.Built.Model;
        var (pick, item, m) = Pick();
        _ = Union(item, text);
        var captured = slots.Capture(new LocalVariable("n", slots.Int)).Value;
        model.GenericCalls.Record(pick, [text, captured]);
        model.GenericCalls.Record(pick, [text, slots.Literal(5)]);
        using var routes = SlotRoutes.Enter([m]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        var instance = Assert.Single(UnionSpecialization.InstancesOf(pick));

        Assert.Same(m, instance[1]);
        Assert.Equal(UnionSpecialization.Name(pick, [text, captured]), UnionSpecialization.Name(pick, [text, slots.Literal(5)]));
        Assert.Equal(UnionSpecialization.Name(pick, instance), UnionSpecialization.Name(pick, [text, captured]));

        using var specializing = TypeEmitter.Specializing(pick, instance);

        Assert.StartsWith($"int {UnionSpecialization.Name(pick, instance)}(int __M, ", MethodEmitter.EmitSignature(pick, new DiagnosticBag()), StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyOfAnotherTypeAtAClosingOfAValueRoutedTypeHasNoReachToItsSlot()
    {
        var model = slots.Refinements.Built.Model;
        var (holder, item) = Holder();
        _ = Union(item, text);
        var closing = slots.Closed(holder, slots.Closed(ring, capacity));
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());

        var instance = Assert.Single(UnionSpecialization.InstancesOf(holder));

        Assert.Same(closing, instance);

        using(TypeEmitter.Specializing(instance))
        {
            Assert.Equal("default", DependentCarrierNames.ValueRead(capacity));
        }

        Assert.Equal("__Capacity", DependentCarrierNames.ValueRead(capacity));

        var refused = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refused.Number.Value);
        Assert.StartsWith(
            "'Capacity' of 'Ring' cannot be read in the copy of 'Holder' emitted for 'Holder<Ring<Capacity>>', which is one C# type for every value of it",
            refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyOfAnotherMethodAtAClosingOfAValueRoutedTypeHasNoReachToItsSlotWhileItsOwnStaysInReach()
    {
        var model = slots.Refinements.Built.Model;
        var (same, tested) = Same();
        _ = Union(tested, text);
        var (pick, item, m) = Pick();
        _ = Union(item, text);
        model.GenericCalls.Record(same, [slots.Closed(ring, capacity)]);
        model.GenericCalls.Record(pick, [text, slots.Capture(new LocalVariable("n", slots.Int)).Value]);
        using var routes = SlotRoutes.Enter([capacity, m]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());

        using(TypeEmitter.Specializing(same, Assert.Single(UnionSpecialization.InstancesOf(same))))
        {
            Assert.Equal("default", DependentCarrierNames.ValueRead(capacity));
        }

        using(TypeEmitter.Specializing(pick, Assert.Single(UnionSpecialization.InstancesOf(pick))))
        {
            Assert.Equal("__M", DependentCarrierNames.ValueRead(m));
        }

        var refused = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refused.Number.Value);
        Assert.StartsWith(
            "'Capacity' of 'Ring' cannot be read in the copy of 'Same' emitted for 'Same<Ring<Capacity>>', which is one C# method for every value of it",
            refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACopyOfTheOwnersOwnMethodWithItsClosingInATypeArgumentHasNoReachToItsSlot(bool isStatic)
    {
        var model = slots.Refinements.Built.Model;
        var (same, tested) = OwnSame(isStatic);
        _ = Union(tested, text);
        model.GenericCalls.Record(same, [slots.Closed(ring, capacity)]);
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());

        using(TypeEmitter.Specializing(same, Assert.Single(UnionSpecialization.InstancesOf(same))))
        {
            Assert.Equal("default", DependentCarrierNames.ValueRead(capacity));
        }

        Assert.Equal("__Capacity", DependentCarrierNames.ValueRead(capacity));

        var refused = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refused.Number.Value);
        Assert.StartsWith(
            "'Capacity' of 'Ring' cannot be read in the copy of 'Same' emitted for 'Same<Ring<Capacity>>', which is one C# method for every value of it standing in its type arguments",
            refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyOfAMethodWithItsOwnSlotInsideATypeArgumentReadsNeitherValueOfIt()
    {
        var model = slots.Refinements.Built.Model;
        var (pick, item, m) = Pick();
        _ = Union(item, text);
        model.GenericCalls.Record(pick, [slots.Closed(ring, m), slots.Literal(5)]);
        using var routes = SlotRoutes.Enter([capacity, m]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());

        var instance = Assert.Single(UnionSpecialization.InstancesOf(pick));

        Assert.Same(m, instance[1]);

        using(TypeEmitter.Specializing(pick, instance))
        {
            Assert.Equal("default", DependentCarrierNames.ValueRead(m));
        }

        Assert.Equal("__M", DependentCarrierNames.ValueRead(m));

        var refused = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refused.Number.Value);
        Assert.StartsWith(
            "'M' of 'Pick' cannot be read in the copy of 'Pick' emitted for 'Pick<Ring<M>, M>', which is one C# method for every value of it standing in its type arguments",
            refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AUnionCarrierNamesAValueRoutedClosingByItsSlotsDomainWhateverStandsInTheSlot()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int)).Value;
        var literal = Union(slots.Closed(ring, slots.Literal(5)), text);
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("Union_Ring_1_Int32_String", UnionCarrierNames.CarrierName(Union(slots.Closed(ring, capacity), text)));
        Assert.Equal("Union_Ring_1_Int32_String", UnionCarrierNames.CarrierName(Union(slots.Closed(ring, capture), text)));
        Assert.Equal("Union_Ring_1_Int32_String", UnionCarrierNames.CarrierName(literal));
    }

    [Fact]
    public void AUnionCarrierNamesACarriedClosingByItsValueAsBefore()
    {
        Assert.Equal("Union_Ring_1_Const_5_String", UnionCarrierNames.CarrierName(Union(slots.Closed(ring, slots.Literal(5)), text)));
    }

    [Fact]
    public void AUnionOverTwoClosingsOfOneValueRoutedTypeIsRefusedWhereItIsSpelled()
    {
        var union = Union(slots.Closed(ring, slots.Literal(5)), slots.Closed(ring, slots.Literal(7)));
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();

        _ = TypeNameEmitter.Emit(union, diagnostics);

        var refused = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0501", refused.Number.Value);
        Assert.Equal(
            "'Ring<5> | Ring<7>' cannot be carried by the C# backend: 'Ring<5>' and 'Ring<7>' are one C# type, so its carrier cannot tell them apart.",
            refused.Message);
    }

    [Fact]
    public void AUnionOverTwoClosingsOfACarriedTypeIsSpelledAsBefore()
    {
        var union = Union(slots.Closed(ring, slots.Literal(5)), slots.Closed(ring, slots.Literal(7)));
        var diagnostics = new DiagnosticBag();

        Assert.Equal("Union_Ring_1_Const_5_Ring_1_Const_7", TypeNameEmitter.Emit(union, diagnostics));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AVariadicReferenceLeavesAValueRoutedSlotOutOfItsArguments()
    {
        var bundle = HandBuiltModel.Class("Bundle<>");
        bundle.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var n = new TypeParameter("N", 0, bundle.Scope, bundle) { IsDependent = true, Domain = (MistType)slots.Int };
        var pack = new TypeParameter("Ts", 1, bundle.Scope, bundle) { PackKind = TypePackKind.Unbracketed };
        bundle.TypeParametersList.Add(n);
        bundle.TypeParametersList.Add(pack);
        var elements = new LiftedType(slots.Refinements.Built.Type("Type"), new TypePackValue([slots.Int, text]));
        var closing = slots.Closed(bundle, slots.Capture(new LocalVariable("n", slots.Int)).Value, elements);
        using var routes = SlotRoutes.Enter([n]);

        Assert.Equal("Probe.Bundle_2<int, string>", MonomorphReference.Emit(closing, TypeNameEmitter.Emit));
    }

    private UnionType Union(params IMistType[] constituents)
    {
        return (UnionType)slots.Refinements.Built.Interner.GetOrCreateUnionType(constituents);
    }

    private (ClassType Pair, TypeParameter Item, TypeParameter N) Pair()
    {
        var pair = HandBuiltModel.Class("Pair<>");
        pair.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var item = new TypeParameter("T", 0, pair.Scope, pair);
        var n = new TypeParameter("N", 1, pair.Scope, pair) { IsDependent = true, Domain = (MistType)slots.Int };
        pair.TypeParametersList.Add(item);
        pair.TypeParametersList.Add(n);
        TypeParameterDomainResolver.ProjectValues(pair);
        _ = slots.Refinements.Built.Model.RegisterType(pair);
        return (pair, item, n);
    }

    private (ClassType Holder, TypeParameter Item) Holder()
    {
        var holder = HandBuiltModel.Class("Holder<>");
        holder.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var item = new TypeParameter("T", 0, holder.Scope, holder);
        holder.TypeParametersList.Add(item);
        _ = slots.Refinements.Built.Model.RegisterType(holder);
        return (holder, item);
    }

    private (Method Same, TypeParameter Tested) Same()
    {
        var owner = HandBuiltModel.Class("Util");
        owner.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var tested = new TypeParameter("T", 0, new Scope(null), null);
        var same = new Method("Same", slots.Refinements.Bool, [new Parameter("v", Union(tested, text))], new MemberModifiers { IsStatic = true }) { DeclaringType = owner, Scope = owner.Scope };
        same.TypeParametersList.Add(tested);
        tested.DeclaringMethod = same;
        owner.MethodsList.Add(same);
        _ = slots.Refinements.Built.Model.RegisterType(owner);
        return (same, tested);
    }

    private (Method Same, TypeParameter Tested) OwnSame(bool isStatic)
    {
        var tested = new TypeParameter("T", 0, new Scope(null), null);
        var same = new Method("Same", slots.Refinements.Bool, [new Parameter("v", Union(tested, text))], new MemberModifiers { IsStatic = isStatic }) { DeclaringType = ring, Scope = ring.Scope };
        same.TypeParametersList.Add(tested);
        tested.DeclaringMethod = same;
        ring.MethodsList.Add(same);
        return (same, tested);
    }

    private (Method Pick, TypeParameter Item, TypeParameter M) Pick()
    {
        var owner = HandBuiltModel.Class("Api");
        owner.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var item = new TypeParameter("T", 0, new Scope(null), null);
        var m = new TypeParameter("M", 1, new Scope(null), null) { IsDependent = true, Domain = (MistType)slots.Int };
        var pick = new Method("Pick", slots.Int, [new Parameter("v", Union(item, text))], new MemberModifiers { IsStatic = true }) { DeclaringType = owner, Scope = owner.Scope };
        pick.TypeParametersList.Add(item);
        pick.TypeParametersList.Add(m);
        item.DeclaringMethod = pick;
        m.DeclaringMethod = pick;
        owner.MethodsList.Add(pick);
        _ = slots.Refinements.Built.Model.RegisterType(owner);
        return (pick, item, m);
    }

    private List<string> Body(MemberType type, ConstructedType instance)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var model = slots.Refinements.Built.Model;

        using(TypeEmitter.Specializing(instance))
        {
            TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);
        }

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }
}