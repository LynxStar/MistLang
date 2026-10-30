using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Text = Mist.Common.Source.SourceText;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class FactoryRelatedLocationBoundsTests
{
    private static readonly MistDiagnostic Probe = DiagnosticFixtures.Descriptor("FIXTURE_RELATED", "placed {0}");
    private static readonly SourceIdentity File = new(new Text("related.mist", "ab\ncd\nef"));
    private static readonly ImmutableArray<Frame> Frames = [new SynthesisFrame("lowering")];

    [Theory]
    [MemberData(nameof(FactoryLocationBoundsTests.InvalidLocations), MemberType = typeof(FactoryLocationBoundsTests))]
    public void BothFactoryOverloadsRejectInvalidRelatedBounds(string shape, int start, int length, bool resolved)
    {
        var primary = new SourceRange(File, 0, 1);
        var before = new RelatedLocation(RelatedRole.Import, new SourcePoint(File, 8));
        var invalid = new RelatedLocation(RelatedRole.PreviousDeclaration, MakeLocation(shape, start, length));
        var after = new RelatedLocation(RelatedRole.Constraint, new FileOnly("constraints.mist"));
        ImmutableArray<RelatedLocation> related = [before, invalid, after];

        var admission = Create(new OriginResolution(primary, Frames), related, resolved);

        Assert.Same(primary, admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        Assert.Equal(3, admission.Occurrence.Related.Length);
        Assert.Equal(before, admission.Occurrence.Related[0]);
        Assert.Equal(after, admission.Occurrence.Related[2]);
        Assert.Equal(invalid.Role, admission.Occurrence.Related[1].Role);
        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Related[1].Location);
        Assert.Null(admission.Occurrence.Related[1].Location.Lines);
        Assert.Equal("placed value", admission.Occurrence.Message);
        var breach = Assert.Single(admission.Breaches);
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, breach.Descriptor);
        Assert.Same(primary, breach.Location);
        Assert.Equal(Frames, breach.Frames);
        Assert.Contains("FIXTURE_RELATED", breach.Message);
        Assert.Contains("Analysis", breach.Message);
        Assert.Contains($"span [{start}, +{length}) does not fit 'related.mist' (8 characters)", breach.Message);
    }

    [Theory]
    [MemberData(nameof(FactoryLocationBoundsTests.ValidLocations), MemberType = typeof(FactoryLocationBoundsTests))]
    public void BothFactoryOverloadsKeepValidRelatedBoundaries(string shape, int start, int length, bool resolved)
    {
        var location = MakeLocation(shape, start, length);
        var related = Enum.GetValues<RelatedRole>().Select(role => new RelatedLocation(role, location)).ToImmutableArray();
        var primary = new FileOnly("primary.mist");

        var admission = Create(new OriginResolution(primary, Frames), related, resolved);

        Assert.Empty(admission.Breaches);
        Assert.Same(primary, admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        Assert.Equal<RelatedLocation>(related, admission.Occurrence.Related);
        foreach(var item in admission.Occurrence.Related)
        {
            Assert.Same(location, item.Location);
            var source = item.Location switch
            {
                ProjectLocation project => project.KeySpan!,
                GeneratedLocation generated => generated.Mapped!,
                _ => item.Location,
            };
            var expectedStart = start == 0 ? new LinePosition(1, 1) : new LinePosition(3, 3);
            Assert.Equal(new LinePositionSpan(expectedStart, new LinePosition(expectedStart.Line, expectedStart.Column + length)), source.Lines);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MultipleRelatedFaultsAndPrimaryFaultProduceOneBoundsIce(bool resolved, bool schemaFault)
    {
        var origin = new OriginResolution(new SourceRange(File, -1, 1), Frames);

        AssertMixedFaults(origin, resolved, schemaFault);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExistingPrimaryFaultIsPreservedAlongsideRelatedFaults(bool schemaFault)
    {
        var origin = new OriginResolution(new Absent(AbsenceReason.OutOfBounds), Frames, "existing primary fault");

        AssertMixedFaults(origin, true, schemaFault);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleRelatedFaultsProduceOneBoundsIceWithAValidPrimary(bool resolved)
    {
        var primary = new SourcePoint(File, 8);
        ImmutableArray<RelatedLocation> related =
        [
            new(RelatedRole.CloneDefinition, new SourceRange(File, -1, 1)),
            new(RelatedRole.Instantiation, new SourcePoint(File, int.MaxValue)),
        ];

        var admission = Create(new OriginResolution(primary, Frames), related, resolved);

        Assert.Same(primary, admission.Occurrence.Location);
        Assert.Equal(related.Select(item => item.Role), admission.Occurrence.Related.Select(item => item.Role));
        Assert.All(admission.Occurrence.Related, item => Assert.Equal(new Absent(AbsenceReason.OutOfBounds), item.Location));
        Assert.Same(InternalDiagnostics.LocationOutOfBounds, Assert.Single(admission.Breaches).Descriptor);
    }

    private static void AssertMixedFaults(OriginResolution origin, bool resolved, bool schemaFault)
    {
        var valid = new RelatedLocation(RelatedRole.DependencyDeclaration, new ProjectLocation("mist.toml", null));
        ImmutableArray<RelatedLocation> related =
        [
            new(RelatedRole.CloneDefinition, new SourceRange(File, int.MaxValue, 1)),
            valid,
            new(RelatedRole.GeneratedOutput, MakeLocation("generated", 0, -1)),
        ];

        var admission = Create(origin, related, resolved, schemaFault);

        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Location);
        Assert.Equal(Frames, admission.Occurrence.Frames);
        Assert.Equal(related.Select(item => item.Role), admission.Occurrence.Related.Select(item => item.Role));
        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Related[0].Location);
        Assert.Equal(valid, admission.Occurrence.Related[1]);
        Assert.Equal(new Absent(AbsenceReason.OutOfBounds), admission.Occurrence.Related[2].Location);
        Assert.Equal(schemaFault ? 2 : 1, admission.Breaches.Length);
        var bounds = Assert.Single(admission.Breaches, item => item.Descriptor == InternalDiagnostics.LocationOutOfBounds);
        Assert.Contains(origin.BoundsFault ?? "span [-1, +1)", bounds.Message);
        Assert.Equal(admission.Occurrence.Location, bounds.Location);
        Assert.Equal(Frames, bounds.Frames);
        if(schemaFault)
        {
            _ = Assert.Single(admission.Breaches, item => item.Descriptor == InternalDiagnostics.ArgumentSchemaBreach);
        }
    }

    private static Admission Create(OriginResolution origin, ImmutableArray<RelatedLocation> related, bool resolved, bool schemaFault = false)
    {
        SlotValue[] arguments = schemaFault ? [] : [new TextValue("value")];
        return resolved
            ? DiagnosticFactory.Create(Probe, origin, PipelineStage.Analysis, arguments, related)
            : DiagnosticFactory.Create(Probe, origin.Location, PipelineStage.Analysis, arguments, related, origin.Frames);
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