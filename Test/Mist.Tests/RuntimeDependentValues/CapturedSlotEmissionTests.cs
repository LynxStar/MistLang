using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
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
}