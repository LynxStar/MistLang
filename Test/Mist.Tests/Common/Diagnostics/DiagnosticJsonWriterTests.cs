using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using MistSourceText = Mist.Common.Source.SourceText;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class DiagnosticJsonWriterTests
{
    [Fact]
    public void DocumentHasStableKeyOrderBytesAndEffectiveCountsWithNativeOutsideCounts()
    {
        var bag = new DiagnosticBag();
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "note", DiagnosticSeverity.Info), new FileOnly("exact/../file.mist"));
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9002", "warning", DiagnosticSeverity.Warning), new Absent(AbsenceReason.Unavailable));
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9003", "error"), new Absent(AbsenceReason.TestOrigin));
        var document = new DiagnosticDocument("mist test", "run", "sample", "failed", bag.ToImmutableArray(),
            [
                new NativeDiagnostic("roslyn", "CS1234", "warning", "native", new GeneratedLocation("out/a.cs", new(new(1, 2), new(1, 4)), null)),
                new NativeDiagnostic("roslyn", "CS8019", "hidden", "native hidden", new GeneratedLocation("out/b.cs", new(new(2, 1), new(2, 4)), null)),
                new NativeDiagnostic("roslyn", "CS9999", "error", "native error", new GeneratedLocation("out/c.cs", new(new(3, 1), new(3, 4)), null)),
            ], DiagnosticSeverity.Error);
        var bytes = DiagnosticJsonWriter.Serialize(document);
        Assert.Equal(bytes, DiagnosticJsonWriter.Serialize(document));
        Assert.Equal((byte)'{', bytes[0]);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Assert.Equal(["schemaVersion", "compiler", "command", "project", "outcome", "counts", "diagnostics", "native"], Keys(root));
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        var counts = root.GetProperty("counts");
        Assert.Equal(["admitted", "errors", "warnings", "info", "hidden"], Keys(counts));
        Assert.Equal([3, 1, 1, 1, 2], counts.EnumerateObject().Select(p => p.Value.GetInt32()));
        var diagnostics = root.GetProperty("diagnostics");
        Assert.Equal([false, false, true], diagnostics.EnumerateArray().Select(d => d.GetProperty("visible").GetBoolean()));
        Assert.Equal("exact/../file.mist", diagnostics[0].GetProperty("location").GetProperty("path").GetString());
        Assert.Equal(["warning", "hidden", "error"], root.GetProperty("native").EnumerateArray().Select(d => d.GetProperty("severity").GetString()));
    }

    [Fact]
    public void OccurrencePreservesTypedSlotsProvenanceAndFixedFieldOrder()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST9001", "{name}", slots:
        [new("name", SlotKind.Name), new("type", SlotKind.Type), new("symbol", SlotKind.Symbol), new("path", SlotKind.Path),
         new("quantity", SlotKind.Quantity), new("flag", SlotKind.Boolean), new("token", SlotKind.Enum), new("text", SlotKind.Text),
         new("sequence", SlotKind.Sequence, SlotKind.Text), new("absent", SlotKind.Optional, SlotKind.Name), new("present", SlotKind.Optional, SlotKind.Name)]);
        var location = new SourceRange(new SourceIdentity(new MistSourceText("source.mist", "abc\r\ndef")), 5, 2);
        var bag = new DiagnosticBag();
        bag.Admit(DiagnosticFactory.Create(descriptor, location, PipelineStage.SemanticModelConstruction,
            [new NameValue("name"), new TypeValue("Standard.Int32", "int"), new SymbolValue("Owner.Member", "Member"), new PathValue("path"),
             new QuantityValue(42), new BooleanValue(true), new EnumValue("SomeToken"), new TextValue("upstream"),
             new SequenceValue(SlotKind.Text, [new TextValue("one"), new TextValue("two")]),
             new OptionalValue(SlotKind.Name, null), new OptionalValue(SlotKind.Name, new NameValue("present"))],
            related: [new RelatedLocation(RelatedRole.PreviousDeclaration, new FileOnly("other.mist"))],
            frames: [new LoweringFrame("await"), new SynthesisFrame("clone"), new OperationFrame("project", ["ref"], "package"), new TruncatedFrame(8, true)],
            cause: 7, native: new ProviderCode("roslyn", "CS1234", DiagnosticSeverity.Error)));
        using var json = JsonDocument.Parse(DiagnosticJsonWriter.Serialize(new("mist", "build", ".", "failed", bag.ToImmutableArray(), [])));
        var d = json.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal(["ordinal", "code", "title", "category", "policy", "severity", "message", "arguments", "location", "related", "frames", "cause", "native", "stage", "visible"], Keys(d));
        Assert.Equal(1, d.GetProperty("ordinal").GetInt32());
        Assert.Equal("semanticModelConstruction", d.GetProperty("stage").GetString());
        Assert.Equal("TYP", d.GetProperty("category").GetString());
        var args = d.GetProperty("arguments");
        Assert.Equal("int", args.GetProperty("type").GetProperty("spelling").GetString());
        Assert.Equal("Standard.Int32", args.GetProperty("type").GetProperty("identity").GetString());
        Assert.Equal("Owner.Member", args.GetProperty("symbol").GetProperty("identity").GetString());
        Assert.Equal(42, args.GetProperty("quantity").GetInt64());
        Assert.True(args.GetProperty("flag").GetBoolean());
        Assert.Equal("someToken", args.GetProperty("token").GetString());
        Assert.Equal("upstream", args.GetProperty("text").GetString());
        Assert.Equal("path", args.GetProperty("path").GetString());
        Assert.Equal(["one", "two"], args.GetProperty("sequence").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(JsonValueKind.Null, args.GetProperty("absent").ValueKind);
        Assert.Equal("present", args.GetProperty("present").GetString());
        Assert.Equal(2, d.GetProperty("location").GetProperty("line").GetInt32());
        Assert.Equal(1, d.GetProperty("location").GetProperty("column").GetInt32());
        Assert.Equal("previousDeclaration", d.GetProperty("related")[0].GetProperty("role").GetString());
        Assert.Equal(["lowering", "synthesis", "operation", "truncated"], d.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("kind").GetString()));
        Assert.Equal(7, d.GetProperty("cause").GetInt32());
        Assert.Equal("CS1234", d.GetProperty("native").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0, "sourceRange", "file.mist(1,2):")]
    [InlineData(1, "sourcePoint", "file.mist(1,2):")]
    [InlineData(2, "fileOnly", "file.mist:")]
    [InlineData(3, "project", "mist.toml:")]
    [InlineData(4, "external", "error")]
    [InlineData(5, "generated", "out/file.cs:")]
    [InlineData(6, "absent", "error")]
    public void LocationsShareExactPathsWithTextWithoutInventedPositions(int shape, string kind, string prefix)
    {
        var file = new SourceIdentity(new MistSourceText("file.mist", "abc"));
        var range = new SourceRange(file, 1, 1);
        Location location = shape switch
        {
            0 => range,
            1 => new SourcePoint(file, 1),
            2 => new FileOnly(file.Path),
            3 => new ProjectLocation("mist.toml", range),
            4 => new ExternalLocation("Assembly", "Symbol"),
            5 => new GeneratedLocation("out/file.cs", new(new(2, 3), new(2, 4)), range),
            _ => new Absent(AbsenceReason.Unavailable),
        };
        var bag = new DiagnosticBag();
        bag.ReportAt(DiagnosticFixtures.Descriptor("MIST9001", "message", title: "Unprinted title"), location);
        var d = Assert.Single(bag.Admitted);
        Assert.StartsWith(prefix, DiagnosticFormatter.Format(d));
        Assert.DoesNotContain("Unprinted title", DiagnosticFormatter.Format(d));
        using var json = JsonDocument.Parse(DiagnosticJsonWriter.Serialize(new("mist", "build", ".", "failed", bag.ToImmutableArray(), [])));
        var encoded = json.RootElement.GetProperty("diagnostics")[0].GetProperty("location");
        Assert.Equal(kind, encoded.GetProperty("kind").GetString());
        if(location.Path is { } path)
        {
            Assert.Equal(path, encoded.GetProperty("path").GetString());
        }
    }

    private static IEnumerable<string> Keys(JsonElement element)
    {
        return element.EnumerateObject().Select(p => p.Name);
    }
}