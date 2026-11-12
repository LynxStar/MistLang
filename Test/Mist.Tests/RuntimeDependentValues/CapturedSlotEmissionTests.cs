using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class CapturedSlotEmissionTests
{
    private readonly HandBuiltSlots slots = new();

    [Fact]
    public void ACaptureInAnArraySlotIsSpelledAsTheErasedTypeIs()
    {
        var diagnostics = new DiagnosticBag();
        var length = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));

        Assert.Equal("int[]", TypeNameEmitter.Emit(slots.ArrayOf(length.Value), diagnostics));
        Assert.Equal("int", TypeNameEmitter.Emit(length.Value, diagnostics));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ACaptureInAUserTypesSlotIsRefused()
    {
        var diagnostics = new DiagnosticBag();
        var ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        var capacity = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));

        _ = TypeNameEmitter.Emit(slots.Closed(ring, capacity.Value), diagnostics);

        var refused = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0488", refused.Number.Value);
        Assert.Contains("cannot spell 'Ring<n>' yet: it is closed with 'n'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestAgainstACapturedClosingNamesItsCapture()
    {
        var length = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));

        Assert.Same(length.Value, ErasedShape.Captured(new TupleType([slots.Int, slots.ArrayOf(length.Value)])));
        Assert.Null(ErasedShape.Captured(slots.ArrayOf(slots.Literal(3))));
    }
}