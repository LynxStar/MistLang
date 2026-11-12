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
    public void ASlotsArgumentStandingAloneIsSpelledAsItsDomain()
    {
        var captured = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive)).Value;
        var literal = slots.Literal(4);
        var unknown = new UnknownValueType(slots.Int);

        Assert.Equal("int", TypeDisplay.Mist(captured));
        Assert.Equal("int", TypeDisplay.Mist(literal));
        Assert.Equal("int", TypeDisplay.Mist(unknown));
        Assert.Equal("int", DiagnosticSnapshot.Type(captured, captured.Name).Spelling);
        Assert.Equal("int", DiagnosticSnapshot.Type(literal, literal.Name).Spelling);
        Assert.Equal("int", DiagnosticSnapshot.Type(unknown, unknown.Name).Spelling);
        Assert.Equal("int[4]", TypeDisplay.Mist(slots.ArrayOf(literal)));
        Assert.Equal(["int", "int"], TypeDisplay.Distinguished([captured, literal]));
    }

    [Fact]
    public void ATypeHoldingNoCaptureKeepsTheCallersSpelling()
    {
        var closing = slots.ArrayOf(slots.Literal(3));

        Assert.Equal(closing.Name, DiagnosticSnapshot.Type(closing, closing.Name).Spelling);
    }
}