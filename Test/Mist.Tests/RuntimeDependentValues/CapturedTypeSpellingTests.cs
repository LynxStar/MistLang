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
    public void ACaptureInsideAnotherTypeIsSpelledAsItsDomainWhileALiteralAndAClosingKeepTheValue()
    {
        var captured = slots.Capture(new LocalVariable("n", slots.ZeroOrPositive)).Value;
        var four = slots.Literal(4);
        var unknown = new UnknownValueType(slots.Int);
        var ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring"), "Capacity", slots.Int);
        var box = HandBuiltModel.Class("Box");
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        var list = HandBuiltModel.Class("List");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        var lambda = new FunctionType([], four);
        var written = slots.Closed(box, four);

        Assert.Equal("int[1]", TypeDisplay.Mist(slots.Closed(slots.Array, captured, slots.Literal(1))));
        Assert.Equal("(int, int)", TypeDisplay.Mist(new TupleType([captured, slots.Int])));
        Assert.Equal("List<int>", TypeDisplay.Mist(slots.Closed(list, captured)));
        Assert.Equal("List<int>", TypeDisplay.Mist(slots.Closed(list, unknown)));
        Assert.Equal("func<int>", TypeDisplay.Mist(new FunctionType([], captured)));
        Assert.Equal("int | string", TypeDisplay.Mist(new UnionType([captured, slots.Refinements.String])));
        Assert.Equal("Box<4>", TypeDisplay.Mist(written));
        Assert.Equal("Box<4>", DiagnosticSnapshot.Type(written, "Box<4>").Spelling);
        Assert.Equal("List<4>", TypeDisplay.Mist(slots.Closed(list, four)));
        Assert.Equal("func<4>", TypeDisplay.Mist(lambda));
        Assert.Equal("func<4>", DiagnosticSnapshot.Type(lambda, lambda.Name).Spelling);
        Assert.Equal("int[n]", TypeDisplay.Mist(slots.ArrayOf(captured)));
        Assert.Equal("Ring<n>", TypeDisplay.Mist(slots.Closed(ring, captured)));
        Assert.Equal("int[4]", TypeDisplay.Mist(slots.ArrayOf(four)));
        Assert.Equal("Ring<4>", TypeDisplay.Mist(slots.Closed(ring, four)));
        Assert.Equal("List<int[n]>", TypeDisplay.Mist(slots.Closed(list, slots.ArrayOf(captured))));
    }

    [Fact]
    public void ATypeHoldingNoCaptureKeepsTheCallersSpelling()
    {
        var closing = slots.ArrayOf(slots.Literal(3));

        Assert.Equal(closing.Name, DiagnosticSnapshot.Type(closing, closing.Name).Spelling);
    }
}