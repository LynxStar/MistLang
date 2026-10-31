using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Text = Mist.Common.Source.SourceText;

namespace Mist.Tests.Common.SourceText;

[Trait("Tier", "1")]
public sealed class OriginMapTests
{
    private const string Generated = "b.AddContent(0, count);";

    private static readonly Text Razor = new("Counter.razor", "<p>@count</p>");

    private static readonly OriginMap Map = new([
        new OriginSegment(0, 16, Razor, 3, 6, Verbatim: false),
        new OriginSegment(16, 5, Razor, 4, 5, Verbatim: true),
        new OriginSegment(21, 2, Razor, 3, 6, Verbatim: false),
    ]);

    [Theory]
    [InlineData(16, 5, 4, 5)]
    [InlineData(17, 3, 5, 3)]
    [InlineData(18, 1, 6, 1)]
    public void AVerbatimOffsetIsShiftedIntoTheOriginal(int offset, int length, int originalOffset, int originalLength)
    {
        Assert.Equal(new SourceRange(new SourceIdentity(Razor), originalOffset, originalLength), Map.Map(offset, length));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    [InlineData(15, 1)]
    [InlineData(21, 2)]
    public void ASynthesizedOffsetAnswersWithItsConstructSpan(int offset, int length)
    {
        Assert.Equal(new SourceRange(new SourceIdentity(Razor), 3, 6), Map.Map(offset, length));
    }

    [Theory]
    [InlineData(16, 0, 4, 0, true)]
    [InlineData(21, 0, 3, 6, false)]
    [InlineData(18, 4, 6, 3, true)]
    [InlineData(14, 4, 3, 6, false)]
    public void AnOffsetOnABoundaryBelongsToTheSegmentItStartsAndASpanIsCutAtItsSegmentsEnd(
        int offset, int length, int originalOffset, int originalLength, bool verbatim)
    {
        var segment = Map.Segments.Single(candidate => candidate.GeneratedStart <= offset && offset < candidate.GeneratedEnd);

        Assert.Equal(verbatim, segment.Verbatim);
        Assert.Equal(new SourceRange(new SourceIdentity(Razor), originalOffset, originalLength), Map.Map(offset, length));
    }

    [Fact]
    public void TheEndOfTheTextMapsThroughTheLastSegment()
    {
        var trailing = new OriginMap([new OriginSegment(0, 5, Razor, 4, 5, Verbatim: true)]);

        Assert.Equal(new SourceRange(new SourceIdentity(Razor), 3, 6), Map.Map(Generated.Length, 0));
        Assert.Equal(new SourceRange(new SourceIdentity(Razor), 9, 0), trailing.Map(5, 0));
    }

    [Theory]
    [InlineData(24, 0)]
    [InlineData(22, 2)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, int.MaxValue)]
    public void AnOffsetPastTheEndIsRefused(int offset, int length)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Map.Map(offset, length));
    }

    [Fact]
    public void EachSegmentNamesItsOwnOriginal()
    {
        var imports = new Text("_Imports.razor", "@using App.Models");
        var map = new OriginMap([
            new OriginSegment(0, 6, imports, 1, 5, Verbatim: false),
            new OriginSegment(6, 10, imports, 7, 10, Verbatim: true),
            new OriginSegment(16, 5, Razor, 4, 5, Verbatim: true),
        ]);

        Assert.Equal(new SourceRange(new SourceIdentity(imports), 7, 3), map.Map(6, 3));
        Assert.Equal(new SourceRange(new SourceIdentity(Razor), 4, 5), map.Map(16, 5));
    }

    [Theory]
    [InlineData(1, 5, 4, 5, true)]
    [InlineData(0, 0, 4, 0, true)]
    [InlineData(0, 5, 10, 5, true)]
    [InlineData(0, 5, -1, 5, false)]
    [InlineData(0, 4, 4, 5, true)]
    public void ASegmentThatLeavesAGapOrDoesNotFitItsOriginalIsRefused(int start, int length, int originalStart, int originalLength, bool verbatim)
    {
        _ = Assert.Throws<ArgumentException>(() => new OriginMap([new OriginSegment(start, length, Razor, originalStart, originalLength, verbatim)]));
    }

    [Fact]
    public void ATextTakesOnlyAMapThatCoversItAndQuotesItsVerbatimSegmentsExactly()
    {
        var translated = new Text("out/translated/Counter.razor.mist", Generated) { Origin = Map };
        var misquoted = Generated.Replace("count", "total", StringComparison.Ordinal);

        Assert.Same(Map, translated.Origin);
        Assert.Null(new Text("Counter.mist", Generated).Origin);
        _ = Assert.Throws<ArgumentException>(() => new Text("short.mist", Generated[..^1]) { Origin = Map });
        _ = Assert.Throws<ArgumentException>(() => new Text("misquoted.mist", misquoted) { Origin = Map });
    }
}