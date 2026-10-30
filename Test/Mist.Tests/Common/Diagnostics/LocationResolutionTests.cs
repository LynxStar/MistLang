using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Text = Mist.Common.Source.SourceText;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class LocationResolutionTests
{
    private const string Lf = "ab\ncd\nef";
    private const string CrLf = "ab\r\ncd\r\nef";
    private const string Cr = "ab\rcd\ref";
    private static readonly MistDiagnostic Probe = DiagnosticFixtures.Descriptor("FIXTURE_PLACE", "placed");

    [Theory]
    [InlineData(Lf, 0, 1, 1, 1, 1, 2)]
    [InlineData(Lf, 3, 2, 2, 1, 2, 3)]
    [InlineData(Lf, 8, 0, 3, 3, 3, 3)]
    [InlineData(CrLf, 4, 2, 2, 1, 2, 3)]
    [InlineData(CrLf, 10, 0, 3, 3, 3, 3)]
    [InlineData(Cr, 3, 2, 2, 1, 2, 3)]
    [InlineData(Cr, 6, 2, 3, 1, 3, 3)]
    [InlineData("a\U0001F600b", 1, 2, 1, 2, 1, 4)]
    public void AValidSpanResolvesToARangeWithDerivedLines(string text, int start, int length, int startLine, int startColumn, int endLine, int endColumn)
    {
        var source = new Text("probe.mist", text);
        var node = new BlockNode { Origin = new ParsedOrigin(source), Span = new Storm.Runtime.SourceSpan(start, length) };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        var range = Assert.IsType<SourceRange>(resolved.Location);
        Assert.Equal((start, length, "probe.mist"), (range.Offset, range.Length, range.Path));
        Assert.Equal(new LinePositionSpan(new LinePosition(startLine, startColumn), new LinePosition(endLine, endColumn)), range.Position());
        Assert.Null(resolved.BoundsFault);
        Assert.Empty(resolved.Frames);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(7, 2)]
    [InlineData(9, 0)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, int.MaxValue)]
    public void AnInvalidSpanIsAbsentOutOfBoundsAndNeverFabricated(int start, int length)
    {
        var source = new Text("probe.mist", Lf);
        var node = new BlockNode { Origin = new ParsedOrigin(source), Span = new Storm.Runtime.SourceSpan(start, length) };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), resolved.Location);
        Assert.Contains("probe.mist", resolved.BoundsFault);
    }

    [Fact]
    public void AnInvalidSpanAdmitsTheOccurrenceAndOneInternalErrorNamingTheProducer()
    {
        var source = new Text("probe.mist", Lf);
        var node = new BlockNode { Origin = new ParsedOrigin(source), Span = new Storm.Runtime.SourceSpan(7, 5) };
        var bag = new DiagnosticBag(PipelineStage.SemanticModelConstruction);

        bag.ReportAtNode(ProgramModelDiagnostics.UnresolvedIdentifier, node, null, "x");

        var admitted = bag.ToImmutableArray();
        Assert.Equal(2, admitted.Length);
        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admitted[0].Location);
        Assert.Equal("The name 'x' could not be found in this scope.", admitted[0].Message);
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, admitted[1].Descriptor);
        Assert.Contains(ProgramModelDiagnostics.UnresolvedIdentifier.Number.Value, admitted[1].Message);
        Assert.Contains("SemanticModelConstruction", admitted[1].Message);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(7, 2)]
    [InlineData(9, 0)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, int.MaxValue)]
    public void AHandBuiltRangeThatDoesNotFitIsAbsentOutOfBoundsThroughTheFactory(int start, int length)
    {
        var file = new SourceIdentity(new Text("probe.mist", Lf));
        var bag = new DiagnosticBag(PipelineStage.Parsing);

        bag.ReportAt(Probe, new SourceRange(file, start, length));

        var admitted = bag.ToImmutableArray();
        Assert.Equal(2, admitted.Length);
        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admitted[0].Location);
        Assert.Equal("error FIXTURE_PLACE: placed", DiagnosticFormatter.Format(admitted[0]));
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, admitted[1].Descriptor);
        Assert.Contains("FIXTURE_PLACE", admitted[1].Message);
        Assert.Contains("Parsing", admitted[1].Message);
        Assert.Contains($"[{start}, +{length})", admitted[1].Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void AHandBuiltPointThatDoesNotFitIsAbsentOutOfBoundsThroughTheFactory(int offset)
    {
        var file = new SourceIdentity(new Text("probe.mist", Lf));
        ImmutableArray<Frame> frames = [new SynthesisFrame("lowering")];

        var admission = DiagnosticFactory.Create(Probe, new SourcePoint(file, offset), PipelineStage.Analysis, [], frames: frames);

        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Location);
        Assert.Equal(frames, admission.Occurrence.Frames);
        Assert.Equal("error FIXTURE_PLACE: placed", DiagnosticFormatter.Format(admission.Occurrence));
        var breach = Assert.Single(admission.Breaches);
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, breach.Descriptor);
        Assert.Contains("Analysis", breach.Message);
    }

    [Fact]
    public void AHandBuiltRangeOrPointThatFitsIsKeptAsSupplied()
    {
        var file = new SourceIdentity(new Text("probe.mist", Lf));
        var first = new SourceRange(file, 0, 1);
        var endOfFile = new SourcePoint(file, 8);
        var bag = new DiagnosticBag(PipelineStage.Parsing);

        bag.ReportAt(Probe, first);
        bag.ReportAt(Probe, endOfFile);

        var admitted = bag.ToImmutableArray();
        Assert.Equal([first, endOfFile], admitted.Select(occurrence => occurrence.Location));
        Assert.Equal("probe.mist(1,1): error FIXTURE_PLACE: placed", DiagnosticFormatter.Format(admitted[0]));
        Assert.Equal("probe.mist(3,3): error FIXTURE_PLACE: placed", DiagnosticFormatter.Format(admitted[1]));
    }

    [Fact]
    public void TestAndUnattachedOriginsAreAbsentWithTheirReason()
    {
        var test = new BlockNode { Origin = TestOrigin.Instance, Span = new Storm.Runtime.SourceSpan(0, 1) };
        var unattached = new BlockNode { Origin = null, Span = new Storm.Runtime.SourceSpan(0, 1) };

        Assert.Equal(new Absent(AbsenceReason.TestOrigin), DiagnosticHelper.ResolveLocation(test).Location);
        Assert.Equal(new Absent(AbsenceReason.NotYetAttached), DiagnosticHelper.ResolveLocation(unattached).Location);
    }

    [Fact]
    public void ASyntheticChainKeepsOneFramePerLinkAndEndsAtItsRoot()
    {
        var source = new Text("probe.mist", Lf);
        var root = new ParsedOrigin(source);
        var inner = new SyntheticOrigin(root, "await insertion");
        var outer = new SyntheticOrigin(inner, "pattern lowering");
        var node = new BlockNode { Origin = outer, Span = new Storm.Runtime.SourceSpan(3, 2) };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        _ = Assert.IsType<SourceRange>(resolved.Location);
        Assert.Equal<Frame>([new SynthesisFrame("pattern lowering"), new SynthesisFrame("await insertion")], resolved.Frames);
    }

    [Fact]
    public void ASyntheticChainWithoutARootIsAbsentWithItsFrames()
    {
        var fromTest = new SyntheticOrigin(TestOrigin.Instance, "lowering");
        var node = new BlockNode { Origin = fromTest, Span = new Storm.Runtime.SourceSpan(0, 0) };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        Assert.Equal(new Absent(AbsenceReason.TestOrigin), resolved.Location);
        Assert.Equal<Frame>([new SynthesisFrame("lowering")], resolved.Frames);
    }

    [Fact]
    public void ACyclicChainIsCutWithATruncatedFrame()
    {
        var first = new SyntheticOrigin(TestOrigin.Instance, "first");
        var second = new SyntheticOrigin(first, "second");
        var cyclic = new SyntheticOrigin(second, "third");
        typeof(SyntheticOrigin).GetField("<DerivedFrom>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(first, cyclic);
        var node = new BlockNode { Origin = cyclic, Span = new Storm.Runtime.SourceSpan(0, 0) };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        Assert.Equal(new Absent(AbsenceReason.SyntheticWithoutRoot), resolved.Location);
        Assert.Equal(new TruncatedFrame(3, Cyclic: true), resolved.Frames[^1]);
        Assert.Equal(["third", "second", "first"], resolved.Frames.OfType<SynthesisFrame>().Select(frame => frame.Reason));
    }

    [Fact]
    public void AChainPastTheDepthLimitIsCutWithATruncatedFrame()
    {
        NodeOrigin origin = new SyntheticOrigin(new ParsedOrigin(new Text("probe.mist", Lf)), "link 0");
        for(var i = 1; i <= 100; i++)
        {
            origin = new SyntheticOrigin(origin, $"link {i}");
        }

        var node = new BlockNode { Origin = origin, Span = new Storm.Runtime.SourceSpan(0, 1) };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        Assert.Equal(new Absent(AbsenceReason.SyntheticWithoutRoot), resolved.Location);
        Assert.Equal(new TruncatedFrame(100, Cyclic: false), resolved.Frames[^1]);
    }

    [Fact]
    public void ContainmentComparesFileIdentityWithTheRange()
    {
        var one = new SourceIdentity(new Text("one.mist", "abcdef"));
        var two = new SourceIdentity(new Text("two.mist", "abcdef"));
        var same = new SourceIdentity(new Text("one.mist", "abcdef"));
        var outer = new SourceRange(one, 1, 4);

        Assert.True(outer.Contains(new SourceRange(same, 2, 2)));
        Assert.True(outer.Contains(new SourceRange(one, 1, 4)));
        Assert.False(outer.Contains(new SourceRange(two, 2, 2)));
        Assert.False(outer.Contains(new SourceRange(one, 0, 2)));
        Assert.False(outer.Contains(new SourceRange(one, 4, 2)));
        Assert.True(outer.Contains(new SourcePoint(same, 3)));
        Assert.True(outer.Contains(new SourcePoint(one, 1)));
        Assert.True(outer.Contains(new SourcePoint(one, 5)));
        Assert.False(outer.Contains(new SourcePoint(one, 0)));
        Assert.False(outer.Contains(new SourcePoint(one, 6)));
        Assert.False(outer.Contains(new SourcePoint(two, 3)));
        Assert.True(new SourcePoint(one, 3).Contains(new SourcePoint(same, 3)));
        Assert.False(new SourcePoint(one, 3).Contains(new SourceRange(one, 3, 1)));
        Assert.True(new Absent(AbsenceReason.TestOrigin).Contains(new Absent(AbsenceReason.TestOrigin)));
        Assert.False(new Absent(AbsenceReason.TestOrigin).Contains(new Absent(AbsenceReason.Unavailable)));
        Assert.False(outer.Contains(new FileOnly("one.mist")));
    }

    [Fact]
    public void SourceIdentityIsThePathAndTheContentHash()
    {
        var a = new SourceIdentity(new Text("one.mist", "abc"));
        var b = new SourceIdentity(new Text("one.mist", "abc"));
        var c = new SourceIdentity(new Text("one.mist", "abd"));
        var d = new SourceIdentity(new Text("two.mist", "abc"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("<unknown>", true)]
    [InlineData("mist.toml", false)]
    public void TheLegacyAdapterMapsAPathWithoutASpan(string path, bool absent)
    {
        var bag = new DiagnosticBag();

        bag.Report(DriverDiagnostics.ProjectFileNotFound, default, path, path);

        var location = Assert.Single(bag.ToImmutableArray()).Location;
        Assert.Equal(absent ? new Absent(AbsenceReason.Unavailable) : new FileOnly(path), location);
    }

    [Fact]
    public void TheTextFormatterAloneRendersTheThreeLineShapes()
    {
        var text = new Text("src/main.mist", "let x = 1;\nlet y = 2;");
        var ranged = DiagnosticFixtures.Occurrence("MIST001", DiagnosticSeverity.Error, PipelineStage.Parsing, "Unexpected token",
            new SourceRange(new SourceIdentity(text), 11, 3));
        var fileOnly = DiagnosticFixtures.Occurrence("MIST002", DiagnosticSeverity.Warning, PipelineStage.Driver, "Odd manifest", new FileOnly("mist.toml"));
        var project = DiagnosticFixtures.Occurrence("MIST003", DiagnosticSeverity.Error, PipelineStage.Driver, "Bad key", new ProjectLocation("mist.toml", null));
        var generated = DiagnosticFixtures.Occurrence("MIST004", DiagnosticSeverity.Error, PipelineStage.Backend, "CS1",
            new GeneratedLocation("out/Program.cs", new LinePositionSpan(new LinePosition(3, 4), new LinePosition(3, 5)), null));
        var external = DiagnosticFixtures.Occurrence("MIST005", DiagnosticSeverity.Error, PipelineStage.Backend, "No spelling", new ExternalLocation("System.Runtime", "System.Span`1"));
        var absent = DiagnosticFixtures.Occurrence("MIST006", DiagnosticSeverity.Info, PipelineStage.Analysis, "Note", new Absent(AbsenceReason.Unavailable));

        Assert.Equal("src/main.mist(2,1): error MIST001: Unexpected token", DiagnosticFormatter.Format(ranged));
        Assert.Equal("mist.toml: warning MIST002: Odd manifest", DiagnosticFormatter.Format(fileOnly));
        Assert.Equal("mist.toml: error MIST003: Bad key", DiagnosticFormatter.Format(project));
        Assert.Equal("out/Program.cs: error MIST004: CS1", DiagnosticFormatter.Format(generated));
        Assert.Equal("error MIST005: No spelling", DiagnosticFormatter.Format(external));
        Assert.Equal("info MIST006: Note", DiagnosticFormatter.Format(absent));
    }
}