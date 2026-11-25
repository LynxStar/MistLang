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

    [Fact]
    public void SourceSpansCarryStartAndEndAcrossLinesAndAPointHasNoLength()
    {
        var file = new SourceIdentity(new MistSourceText("file.mist", "ab\ncdef\ngh"));
        var spanning = Admit(new SourceRange(file, 1, 6));

        var range = Encode(spanning).GetProperty("location");
        var point = Encode(Admit(new SourcePoint(file, 4))).GetProperty("location");

        Assert.Equal(["kind", "path", "line", "column", "endLine", "endColumn", "offset", "length"], Keys(range));
        Assert.Equal([1, 2, 2, 5, 1, 6], Numbers(range));
        Assert.Equal(["kind", "path", "line", "column", "endLine", "endColumn", "offset"], Keys(point));
        Assert.Equal([2, 2, 2, 2, 4], Numbers(point));
        Assert.Equal("file.mist(1,2): error MIST9001: message", DiagnosticFormatter.Format(spanning));
    }

    [Fact]
    public void ProjectAndGeneratedLocationsNestTheirSourceSpanOrWriteNull()
    {
        var key = new SourceRange(new SourceIdentity(new MistSourceText("mist.toml", "[package]\nname = 1")), 10, 4);
        var native = new LinePositionSpan(new(3, 5), new(4, 2));

        var keyed = Encode(Admit(new ProjectLocation("mist.toml", key))).GetProperty("location");
        var unkeyed = Encode(Admit(new ProjectLocation("mist.toml", null))).GetProperty("location");
        var mapped = Encode(Admit(new GeneratedLocation("out/a.cs", native, key))).GetProperty("location");
        var unmapped = Encode(Admit(new GeneratedLocation("out/a.cs", native, null))).GetProperty("location");

        Assert.Equal(["kind", "path", "keySpan"], Keys(keyed));
        Assert.Equal("sourceRange", keyed.GetProperty("keySpan").GetProperty("kind").GetString());
        Assert.Equal([2, 1, 2, 5, 10, 4], Numbers(keyed.GetProperty("keySpan")));
        Assert.Equal(JsonValueKind.Null, unkeyed.GetProperty("keySpan").ValueKind);
        Assert.Equal(["kind", "path", "line", "column", "endLine", "endColumn", "mapped"], Keys(mapped));
        Assert.Equal("out/a.cs", mapped.GetProperty("path").GetString());
        Assert.Equal([3, 5, 4, 2], Numbers(mapped));
        Assert.Equal("mist.toml", mapped.GetProperty("mapped").GetProperty("path").GetString());
        Assert.Equal([2, 1, 2, 5, 10, 4], Numbers(mapped.GetProperty("mapped")));
        Assert.Equal(JsonValueKind.Null, unmapped.GetProperty("mapped").ValueKind);
    }

    [Fact]
    public void AnExternalLocationNamesItsAssemblyAndSymbolWithoutAPath()
    {
        var external = Encode(Admit(new ExternalLocation("System.Runtime", "System.String.Length"))).GetProperty("location");

        Assert.Equal(["kind", "assembly", "symbol"], Keys(external));
        Assert.Equal("System.Runtime", external.GetProperty("assembly").GetString());
        Assert.Equal("System.String.Length", external.GetProperty("symbol").GetString());
    }

    [Theory]
    [InlineData(AbsenceReason.NotYetAttached, "notYetAttached")]
    [InlineData(AbsenceReason.TestOrigin, "testOrigin")]
    [InlineData(AbsenceReason.SyntheticWithoutRoot, "syntheticWithoutRoot")]
    [InlineData(AbsenceReason.OutOfBounds, "outOfBounds")]
    [InlineData(AbsenceReason.Unavailable, "unavailable")]
    public void AnAbsentLocationCarriesOnlyItsLowerCamelCaseReason(AbsenceReason reason, string token)
    {
        var absent = Encode(Admit(new Absent(reason))).GetProperty("location");

        Assert.Equal(["kind", "reason"], Keys(absent));
        Assert.Equal(token, absent.GetProperty("reason").GetString());
    }

    [Fact]
    public void InvariantStormEntryAndWithholdingFramesWriteTheirOwnFields()
    {
        var member = new SymbolValue("System.Span`1.Item", "Item");
        var diagnostic = Admit(
            new Absent(AbsenceReason.Unavailable),
            new InvariantFrame(new EnumValue("binary operator"), new NameValue("Power")),
            new StormEntryFrame(12, 3, "@@@"),
            new WithholdingFrame(member, new EnumValue("UnresolvableType"), new TypeValue("System.Span`1", "Span<T>")),
            new WithholdingFrame(member, new EnumValue("NonPublic")));

        var frames = Encode(diagnostic).GetProperty("frames");

        Assert.Equal(["kind", "operation", "detail"], Keys(frames[0]));
        Assert.Equal(["invariant", "binary operator", "Power"], frames[0].EnumerateObject().Select(p => p.Value.GetString()));
        Assert.Equal(["kind", "offset", "length", "spelling"], Keys(frames[1]));
        Assert.Equal("stormEntry", frames[1].GetProperty("kind").GetString());
        Assert.Equal([12, 3], Numbers(frames[1]));
        Assert.Equal("@@@", frames[1].GetProperty("spelling").GetString());
        Assert.Equal(["kind", "member", "reason", "offending"], Keys(frames[2]));
        Assert.Equal("withholding", frames[2].GetProperty("kind").GetString());
        Assert.Equal("Item", frames[2].GetProperty("member").GetProperty("spelling").GetString());
        Assert.Equal("System.Span`1.Item", frames[2].GetProperty("member").GetProperty("identity").GetString());
        // Requirement 11.6 asks for lower-case enum tokens, but this frame passes the producer's spelling through; the casing is left to that decision.
        Assert.Equal("unresolvableType", frames[2].GetProperty("reason").GetString(), ignoreCase: true);
        Assert.Equal("Span<T>", frames[2].GetProperty("offending").GetProperty("spelling").GetString());
        Assert.Equal("System.Span`1", frames[2].GetProperty("offending").GetProperty("identity").GetString());
        Assert.Equal(JsonValueKind.Null, frames[3].GetProperty("offending").ValueKind);
    }

    private static Diagnostic Admit(Location location, params Frame[] frames)
    {
        var bag = new DiagnosticBag();
        bag.Admit(DiagnosticFactory.Create(
            DiagnosticFixtures.Descriptor("MIST9001", "message"), location, PipelineStage.Driver, [], frames: [.. frames]));
        return Assert.Single(bag.Admitted);
    }

    private static JsonElement Encode(Diagnostic diagnostic)
    {
        using var json = JsonDocument.Parse(DiagnosticJsonWriter.Serialize(new("mist", "build", ".", "failed", [diagnostic], [])));
        return json.RootElement.GetProperty("diagnostics")[0].Clone();
    }

    private static IEnumerable<int> Numbers(JsonElement element)
    {
        return element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).Select(p => p.Value.GetInt32());
    }

    private static IEnumerable<string> Keys(JsonElement element)
    {
        return element.EnumerateObject().Select(p => p.Name);
    }
}