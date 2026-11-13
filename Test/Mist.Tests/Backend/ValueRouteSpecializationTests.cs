using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
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
/// without it; a union carrier names a closing of the type by the slot's domain whatever stands in the slot,
/// and refuses two closings of one such type; a variadic reference leaves the slot out of its arguments.
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