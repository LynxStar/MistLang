using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Text = Mist.Common.Source.SourceText;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class FactoryLocationBoundsTests
{
    private static readonly MistDiagnostic Probe = DiagnosticFixtures.Descriptor("FIXTURE_BOUNDS", "placed");
    private static readonly SourceIdentity File = new(new Text("probe.mist", "ab\ncd\nef"));
    private static readonly ImmutableArray<Frame> Frames = [new SynthesisFrame("lowering")];

    public static IEnumerable<object[]> InvalidLocations()
    {
        foreach(var resolved in new[] { false, true })
        {
            foreach(var shape in new[] { "range", "project", "generated" })
            {
                foreach(var (start, length) in new[] { (-1, 1), (0, -1), (7, 2), (9, 0), (int.MaxValue, 1), (1, int.MaxValue) })
                {
                    yield return [shape, start, length, resolved];
                }
            }

            foreach(var offset in new[] { -1, 9, int.MaxValue })
            {
                yield return ["point", offset, 0, resolved];
            }
        }
    }

    public static IEnumerable<object[]> ValidLocations()
    {
        foreach(var resolved in new[] { false, true })
        {
            foreach(var shape in new[] { "range", "point", "project", "generated" })
            {
                yield return [shape, 0, shape == "point" ? 0 : 1, resolved];
                yield return [shape, 8, 0, resolved];
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidLocations))]
    public void BothFactoryOverloadsRejectInvalidSourceBounds(string shape, int start, int length, bool resolved)
    {
        var location = MakeLocation(shape, start, length);

        var admission = Create(location, resolved);

        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        Assert.Null(admission.Occurrence.Location.Lines);
        Assert.Equal("error FIXTURE_BOUNDS: placed", DiagnosticFormatter.Format(admission.Occurrence));
        var breach = Assert.Single(admission.Breaches);
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, breach.Descriptor);
        Assert.Equal(admission.Occurrence.Location, breach.Location);
        Assert.Equal(Frames, breach.Frames);
        Assert.Contains("FIXTURE_BOUNDS", breach.Message);
        Assert.Contains("Analysis", breach.Message);
        Assert.Contains($"span [{start}, +{length}) does not fit 'probe.mist' (8 characters)", breach.Message);
    }

    [Theory]
    [MemberData(nameof(ValidLocations))]
    public void BothFactoryOverloadsKeepValidBoundaries(string shape, int start, int length, bool resolved)
    {
        var location = MakeLocation(shape, start, length);

        var admission = Create(location, resolved);

        Assert.Same(location, admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        Assert.Empty(admission.Breaches);
        var expected = shape switch
        {
            "project" => "mist.toml: error FIXTURE_BOUNDS: placed",
            _ when start == 0 => "probe.mist(1,1): error FIXTURE_BOUNDS: placed",
            _ => "probe.mist(3,3): error FIXTURE_BOUNDS: placed",
        };
        Assert.Equal(expected, DiagnosticFormatter.Format(admission.Occurrence));
        var expectedStart = shape == "generated" ? new LinePosition(2, 3)
            : start == 0 ? new LinePosition(1, 1) : new LinePosition(3, 3);
        var expectedEnd = shape == "generated" ? new LinePosition(2, 4)
            : new LinePosition(expectedStart.Line, expectedStart.Column + length);
        Assert.Equal(new LinePositionSpan(expectedStart, expectedEnd), admission.Occurrence.Location.Lines);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("invalid")]
    [InlineData("valid")]
    public void AnExistingBoundsFaultIsPreservedAndReportedExactlyOnce(string shape)
    {
        var resolved = DiagnosticHelper.Place(new SourceRange(File, -1, 1), Frames);
        var location = shape switch
        {
            "invalid" => new SourceRange(File, int.MaxValue, 1),
            "valid" => new SourceRange(File, 0, 1),
            _ => resolved.Location,
        };
        var origin = resolved with { Location = location };

        var admission = DiagnosticFactory.Create(Probe, origin, PipelineStage.Analysis, []);

        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        var breach = Assert.Single(admission.Breaches);
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, breach.Descriptor);
        Assert.Contains(resolved.BoundsFault!, breach.Message);
        Assert.Equal(Frames, breach.Frames);
        Assert.Equal(admission.Occurrence.Location, breach.Location);
    }

    private static Admission Create(Location location, bool resolved)
    {
        return resolved
            ? DiagnosticFactory.Create(Probe, new OriginResolution(location, Frames), PipelineStage.Analysis, [])
            : DiagnosticFactory.Create(Probe, location, PipelineStage.Analysis, [], frames: Frames);
    }

    private static Location MakeLocation(string shape, int start, int length)
    {
        var range = new SourceRange(File, start, length);
        return shape switch
        {
            "range" => range,
            "point" => new SourcePoint(File, start),
            "project" => new ProjectLocation("mist.toml", range),
            "generated" => new GeneratedLocation("out.cs", new LinePositionSpan(new LinePosition(2, 3), new LinePosition(2, 4)), range),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}