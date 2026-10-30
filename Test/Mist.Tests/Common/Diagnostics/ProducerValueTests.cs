using System.Collections.Immutable;
using System.Text.Json;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class ProducerValueTests
{
    [Fact]
    public void WideIntegerFactsKeepTheirValueInTextAndJson()
    {
        var value = (Int128)ulong.MaxValue + 1;
        var bag = new DiagnosticBag();
        bag.ReportTyped(
            CommandDiagnostics.LexerRoundTripFailed, new Absent(AbsenceReason.TestOrigin),
            new PathValue("wide.mist"), new QuantityValue(value), new QuantityValue(0), new QuantityValue(0),
            new OptionalValue(SlotKind.Name, null), new OptionalValue(SlotKind.Name, null));
        var finding = Assert.Single(bag.Admitted);
        Assert.Contains("18446744073709551616", finding.Message);
        Assert.Equal(value, Assert.IsType<QuantityValue>(finding.Arguments[1]).Value);
        using var document = JsonDocument.Parse(DiagnosticJsonWriter.Serialize(
            new DiagnosticDocument("test", "lex", "probe", "failed", [finding], [])));
        Assert.Equal("18446744073709551616", document.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("arguments").GetProperty("originalLength").GetRawText());
    }

    [Fact]
    public void OrderedSequencesRenderPresentationWithoutChangingSnapshots()
    {
        var items = ImmutableArray.Create<SlotValue>(new TypeValue("a:Int32", "int"), new TypeValue("b:String", "string"));
        var sequence = new SequenceValue(SlotKind.Type, items, " or ", "'", "'");

        Assert.Equal("'int' or 'string'", DiagnosticRenderer.Render(sequence));
        Assert.Equal(items, sequence.Items);
    }
}