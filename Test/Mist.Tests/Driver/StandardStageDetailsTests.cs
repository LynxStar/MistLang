using System.Globalization;
using Mist.Driver;
using Mist.ProgramAnalysis;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class StandardStageDetailsTests
{
    [Fact]
    public void TheModelStageReportsItsConstructCount()
    {
        Assert.Equal(["3 top-level constructs"], StandardStageDetails.Model(3));
    }

    [Fact]
    public void AnalysisOpensWithTheAnalyzersAndTheScopesTheyRanOverByKind()
    {
        var details = StandardStageDetails.Analysis(Timings(), TimeSpan.FromSeconds(2));

        Assert.Equal("2 analyzers over 4 scopes: 1 program, 2 clone conversions, 1 cloned types", details[0]);
    }

    [Fact]
    public void AnalysisListsEachAnalyzerByIdAndTypeSoASharedIdStaysTwoLines()
    {
        var details = StandardStageDetails.Analysis(Timings(), TimeSpan.FromSeconds(2));

        Assert.Equal(["shared (First)  1.50s", "shared (Second)  0.25s"], details.Skip(1).Take(2));
    }

    [Fact]
    public void AnalysisEndsWithTheStageTimeNoAnalyzerAccountsFor()
    {
        var details = StandardStageDetails.Analysis(Timings(), TimeSpan.FromSeconds(2));

        Assert.Equal("outside analyzers  0.25s", details[^1]);
        Assert.Equal(4, details.Count);
    }

    [Fact]
    public void TimeOutsideAnalyzersIsNeverNegative()
    {
        var details = StandardStageDetails.Analysis(Timings(), TimeSpan.FromSeconds(1));

        Assert.Equal("outside analyzers  0.00s", details[^1]);
    }

    [Fact]
    public void SecondsAreFormattedTheSameUnderACommaDecimalCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var details = StandardStageDetails.Analysis(Timings(), TimeSpan.FromSeconds(2));

            Assert.Equal(["shared (First)  1.50s", "shared (Second)  0.25s", "outside analyzers  0.25s"], details.Skip(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EmissionReportsTheFilesWrittenThenTheArtifact()
    {
        Assert.Equal(["9 .cs files written", "bin/Probe.dll"], StandardStageDetails.Emission(9, "bin/Probe.dll"));
    }

    [Fact]
    public void EmissionWithoutAnArtifactReportsOnlyTheFilesWritten()
    {
        Assert.Equal(["4 .cs files written"], StandardStageDetails.Emission(4, null));
    }

    [Fact]
    public void EmissionReportsDeployedRuntimeAssetsOnlyWhenThereAreAny()
    {
        Assert.Equal(["9 .cs files written", "bin/Probe.dll", "3 runtime assets deployed"], StandardStageDetails.Emission(9, "bin/Probe.dll", 3));
        Assert.Equal(["9 .cs files written", "bin/Probe.dll"], StandardStageDetails.Emission(9, "bin/Probe.dll", 0));
    }

    private static AnalysisTimings Timings()
    {
        var timings = new AnalysisTimings();
        timings.RecordScope(AnalysisScopeKind.Program);
        timings.RecordScope(AnalysisScopeKind.CloneConversion);
        timings.RecordScope(AnalysisScopeKind.CloneConversion);
        timings.RecordScope(AnalysisScopeKind.ClonedType);
        timings.Record(new First(), TimeSpan.FromSeconds(1.5));
        timings.Record(new Second(), TimeSpan.FromSeconds(0.25));

        return timings;
    }

    private sealed class First : IProgramAnalyzer
    {
        public string Id => "shared";

        public void Analyze(AnalysisContext context)
        {
        }
    }

    private sealed class Second : IProgramAnalyzer
    {
        public string Id => "shared";

        public void Analyze(AnalysisContext context)
        {
        }
    }
}