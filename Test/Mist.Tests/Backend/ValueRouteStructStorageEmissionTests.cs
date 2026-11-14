using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// An array creation or a stack allocation whose element holds a value-routed struct by value (runtime-dependent-values
/// design, <em>Identity by value: the first delivery</em>): every element is the closing's stamp, made once with the
/// slot's value, a literal, a dependent parameter in scope or a capture, and copied into each element. A struct that
/// holds the closing in a field starts from its own default with the inner stamp inside it; one with a constructor,
/// and so no default, is built from its parts. A class element, and a struct whose slot is carried in the C# type, keep the
/// CLR zero.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteStructStorageEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly StructType cell;
    private readonly TypeParameter n;
    private readonly Expression size;

    public ValueRouteStructStorageEmissionTests()
    {
        cell = Cell(slots, out n);
        size = HandBuiltModel.Literal(slots.Int, 2L);
    }

    [Fact]
    public void AnArrayAndAStackAllocationOfALiteralClosingStampEveryElementOnce()
    {
        using var routes = SlotRoutes.Enter([n]);
        var seven = slots.Closed(cell, slots.Literal(7));

        Assert.Equal("MistArray.Stamp(new Probe.Cell[2], Probe.Cell.__CreateDefault(7))", Emit(Create(seven)));
        Assert.Equal("MistArray.Stamp(stackalloc Probe.Cell[2], Probe.Cell.__CreateDefault(7))", Emit(Allocate(seven)));
    }

    [Fact]
    public void ADependentParameterInScopeStampsItsOwnValue()
    {
        var m = slots.Dependent("M");
        using var routes = SlotRoutes.Enter([n, m]);

        Assert.Equal("MistArray.Stamp(new Probe.Cell[2], Probe.Cell.__CreateDefault(__M))", Emit(Create(slots.Closed(cell, m))));
        Assert.Equal("MistArray.Stamp(stackalloc Probe.Cell[2], Probe.Cell.__CreateDefault(__M))", Emit(Allocate(slots.Closed(cell, m))));
    }

    [Fact]
    public void ACaptureStampsItsInitializerOnce()
    {
        var capture = slots.Capture(new LocalVariable("k", slots.Int));
        using var routes = SlotRoutes.Enter([n]);
        var element = slots.Closed(cell, capture.Value);

        Assert.Equal("MistArray.Stamp(new Probe.Cell[2], Probe.Cell.__CreateDefault(k))", Emit(Create(element, capture)));
        Assert.Equal("MistArray.Stamp(stackalloc Probe.Cell[2], Probe.Cell.__CreateDefault(k))", Emit(Allocate(element, capture)));
    }

    [Fact]
    public void AStructHoldingTheClosingByValueStampsItsDefaultWithTheInnerStampInside()
    {
        var holder = GenericStruct("Holder<>");
        using var routes = SlotRoutes.Enter([n]);
        var element = slots.Closed(holder, slots.Closed(cell, slots.Literal(7)));

        var emitted = Emit(Create(element));

        Assert.StartsWith("MistArray.Stamp(new Probe.Holder<Probe.Cell>[2], Probe.Holder<Probe.Cell>.__CreateDefault(", emitted, StringComparison.Ordinal);
        Assert.Contains("Probe.Cell.__CreateDefault(7)", emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("__CreateDefault(7)").Length - 1);
    }

    [Fact]
    public void AStructWithAConstructorHoldingTheClosingIsBuiltFromItsParts()
    {
        var made = GenericStruct("Made<>", constructed: true);
        using var routes = SlotRoutes.Enter([n]);
        var element = slots.Closed(made, slots.Closed(cell, slots.Literal(7)));

        Assert.Equal(
            "MistArray.Stamp(new Probe.Made<Probe.Cell>[2], Probe.Made<Probe.Cell>.__FromParts(Probe.Cell.__CreateDefault(7), default(Mist.Emission.CSharp.__StructInit)))",
            Emit(Create(element)));
    }

    [Fact]
    public void AClassElementAndAnUnroutedStructKeepTheZeroedStorage()
    {
        var ring = Ring(slots, out var capacity);
        var plain = (StructType)HandBuiltSlots.Generic(HandBuiltModel.Struct("Plain<>"), "K", slots.Int);
        using var routes = SlotRoutes.Enter([n, capacity]);

        Assert.Equal("new Probe.Ring[2]", Emit(Create(slots.Closed(ring, slots.Literal(7)))));
        Assert.Equal("new Probe.Plain<Const_7>[2]", Emit(Create(slots.Closed(plain, slots.Literal(7)))));
    }

    [Fact]
    public void TheRuntimeHelperIsWrittenOnlyForWhatAProgramUsed()
    {
        ArrayLowering.Reset();
        _ = ArrayLowering.Stamp();
        var stamped = new CSharpWriter();
        ArrayLowering.Emit(stamped);

        Assert.True(ArrayLowering.Used());
        Assert.Contains("public static T[] Stamp<T>(T[] storage, T stamp)", stamped.ToString(), StringComparison.Ordinal);
        Assert.Contains("public static System.Span<T> Stamp<T>(System.Span<T> storage, T stamp)", stamped.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Generate<T>", stamped.ToString(), StringComparison.Ordinal);

        ArrayLowering.Reset();
        Assert.False(ArrayLowering.Used());
    }

    private ArrayCreationExpression Create(IMistType element, params SlotCapture[] captures)
    {
        return new ArrayCreationExpression(HandBuiltModel.Node(), element, size, slots.Closed(slots.Array, element, slots.Literal(2))) { Captures = captures };
    }

    private StackallocExpression Allocate(IMistType element, params SlotCapture[] captures)
    {
        return new StackallocExpression(HandBuiltModel.Node(), element, size, slots.Closed(slots.Array, element, slots.Literal(2))) { Captures = captures };
    }
}