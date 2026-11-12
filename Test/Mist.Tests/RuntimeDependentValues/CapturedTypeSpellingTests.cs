using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class CapturedTypeSpellingTests
{
    private readonly HandBuiltSlots slots = new();

    [Fact]
    public void ATypeHoldingACaptureIsSpelledAsWrittenWhateverTheCallerChose()
    {
        var length = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive));
        var closing = slots.ArrayOf(length.Value);
        var pair = new TupleType([slots.Int, closing]);

        Assert.Contains('@', closing.Name);
        Assert.Equal("int[n]", DiagnosticSnapshot.Type(closing, closing.Name).Spelling);
        Assert.Equal("(int, int[n])", DiagnosticSnapshot.Type(pair, pair.Name).Spelling);
    }

    [Fact]
    public void ATypeHoldingNoCaptureKeepsTheCallersSpelling()
    {
        var closing = slots.ArrayOf(slots.Literal(3));

        Assert.Equal(closing.Name, DiagnosticSnapshot.Type(closing, closing.Name).Spelling);
    }
}