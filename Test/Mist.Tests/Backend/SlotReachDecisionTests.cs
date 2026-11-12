using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Where <see cref="SlotReach"/> says a value-routed slot's bare read lands: the hidden name by default,
/// an operand's member inside an operator, the closing's value inside a restated default, and a refusal
/// where nothing carries it.
/// </summary>
[Trait("Tier", "1")]
public sealed class SlotReachDecisionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;

    public SlotReachDecisionTests()
    {
        ring = (ClassType)HandBuiltSlots.Generic(new ClassType("Ring<>", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()), "Capacity", slots.Int);
        TypeParameterDomainResolver.ProjectValues(ring);
        capacity = ring.TypeParametersList[0];
    }

    [Fact]
    public void AReadOutsideAnyReachIsTheHiddenName()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("__Capacity", SlotReach.Read(capacity));
        Assert.Equal("__Capacity", DependentCarrierNames.ValueRead(capacity));
    }

    [Fact]
    public void AnOperatorReachReadsTheSlotFromTheFirstOperandThatCarriesIt()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var closing = slots.Closed(ring, capacity);
        var plus = Operator(new Parameter("k", slots.Int), new Parameter("a", closing), new Parameter("b", closing));
        var sized = Operator(new Parameter("a", slots.ArrayOf(capacity)), new Parameter("b", closing));

        using(SlotReach.OfOperator(plus))
        {
            Assert.Equal("a.__Capacity", SlotReach.Read(capacity));
        }

        using(SlotReach.OfOperator(sized))
        {
            Assert.Equal("a.Length", SlotReach.Read(capacity));
        }

        Assert.Equal("__Capacity", SlotReach.Read(capacity));
    }

    [Fact]
    public void AnOperatorReachWithNoCarryingOperandRefusesTheRead()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());
        var from = new CastOperator(slots.Closed(ring, capacity), new Parameter("v", slots.Int), isImplicit: true, new MemberModifiers { IsStatic = true }) { DeclaringType = ring };

        using(SlotReach.OfOperator(from))
        {
            Assert.Equal("default", SlotReach.Read(capacity));
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refusal.Number.Value);
        Assert.StartsWith("'Capacity' of 'Ring' cannot be read in the conversion from 'int', whose operand does not carry it", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedReachRefusesTheEnclosingTypesSlotAndLeavesTheNestedTypesOwn()
    {
        var inner = (ClassType)HandBuiltSlots.Generic(new ClassType("Inner<>", "Probe", new Scope(ring.Scope), ring, new BodyTypeTypeDeclarationNode()), "M", slots.Int);
        var m = inner.TypeParametersList[0];
        using var routes = SlotRoutes.Enter([capacity, m]);
        var diagnostics = new DiagnosticBag();
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());

        using(SlotReach.OfNested(inner))
        {
            Assert.Equal("__M", SlotReach.Read(m));
            Assert.Equal("default", SlotReach.Read(capacity));
        }

        Assert.Null(SlotReach.OfNested(ring));
        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refusal.Number.Value);
        Assert.StartsWith("'Capacity' of 'Ring' cannot be read in the nested type 'Inner'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAttributeArgumentReachRefusesEverySlot()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        var diagnostics = new DiagnosticBag();
        using var emission = EmissionDiagnostics.Enter(diagnostics, new BodyTypeTypeDeclarationNode());

        using(SlotReach.OfAttributeArgument())
        {
            Assert.Equal("default", SlotReach.Read(capacity));
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0499", refusal.Number.Value);
        Assert.Contains("cannot be read in an attribute argument, which is a constant", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARestatedDefaultReachReadsTheImplementingClosingsValue()
    {
        var sized = (AbstractType)HandBuiltSlots.Generic(new AbstractType("ISized<>", "Probe", new Scope(null), null, null), "N", slots.Int);
        var n = sized.TypeParametersList[0];
        var twice = new Method("Twice", slots.Int, [], default) { DeclaringType = sized };
        var flex = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Flex<>"), "M", slots.Int);
        using var routes = SlotRoutes.Enter([n, flex.TypeParametersList[0]]);

        using(SlotReach.OfRestated(twice, [slots.Closed(sized, slots.Literal(8))], new DiagnosticBag()))
        {
            Assert.Equal("8", SlotReach.Read(n));
        }

        using(SlotReach.OfRestated(twice, [slots.Closed(sized, flex.TypeParametersList[0])], new DiagnosticBag()))
        {
            Assert.Equal("__M", SlotReach.Read(n));
        }
    }

    private Operator Operator(params Parameter[] parameters)
    {
        return new Operator(OperatorKind.Plus, slots.Int, [.. parameters], new MemberModifiers { IsStatic = true }) { DeclaringType = ring };
    }
}