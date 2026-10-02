using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class ResolutionDiagnosticsTests
{
    [Fact]
    public void AnErrorJoinsASucceededResultWithoutFailingIt()
    {
        var built = new CompilerResult(true, "bin/Probe.dll", []);

        var joined = new ResolutionDiagnostics([Error("MIST_PKG_002")], 0).JoinInto(built);

        Assert.True(joined.Succeeded);
        Assert.Equal("bin/Probe.dll", joined.PrimaryOutputArtifactPath);
        Assert.Equal(["MIST_PKG_002"], Ids(joined));
    }

    [Fact]
    public void AFailedResultStaysFailedAndKeepsBothErrors()
    {
        var refused = new CompilerResult(false, null, [Error("MIST_PM_UNRESOLVED_TYPE")]);

        var joined = new ResolutionDiagnostics([Error("MIST_PKG_002")], 0).JoinInto(refused);

        Assert.False(joined.Succeeded);
        Assert.Equal(["MIST_PKG_002", "MIST_PM_UNRESOLVED_TYPE"], Ids(joined));
    }

    [Fact]
    public void TheyGoBackAfterWhatPrecededThemAndBeforeWhatFollowed()
    {
        var result = new CompilerResult(false, null, [Error("MIST_FILE_ERROR"), Error("MIST_LEX"), Error("MIST_PARSE")]);

        var joined = new ResolutionDiagnostics([Error("MIST_PKG_002"), Error("MIST_FWREF_002")], 1).JoinInto(result);

        Assert.Equal(["MIST_FILE_ERROR", "MIST_PKG_002", "MIST_FWREF_002", "MIST_LEX", "MIST_PARSE"], Ids(joined));
    }

    [Fact]
    public void AResultShorterThanWhatPrecededThemTakesThemLast()
    {
        var result = new CompilerResult(false, null, [Error("MIST_FILE_ERROR")]);

        var joined = new ResolutionDiagnostics([Error("MIST_PKG_002")], 3).JoinInto(result);

        Assert.Equal(["MIST_FILE_ERROR", "MIST_PKG_002"], Ids(joined));
    }

    private static IEnumerable<string> Ids(CompilerResult result)
    {
        return result.Diagnostics.Select(diagnostic => diagnostic.Id.Value);
    }

    private static Diagnostic Error(string id)
    {
        return new Diagnostic(
            new DiagnosticId(id), DiagnosticSeverity.Error, PipelineStage.Driver, "probe", default, "probe.mist", null);
    }
}