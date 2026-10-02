using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class StandardPipelineProgressTests
{
    private readonly RecordingProgressReporter reporter = new();
    private readonly DiagnosticBag diagnostics = new();
    private readonly DiagnosticBag resolution = new();
    private readonly List<string> ran = [];

    [Fact]
    public void ASuccessfulRunStartsAndCompletesStagesFourToSixInOrder()
    {
        var emitted = new CompilerResult(true, "bin/Probe.dll", []);

        var result = Pipeline(emit: (_, _, _) => new Emission(emitted, 3)).Execute(Input());

        Assert.Same(emitted, result);
        Assert.Equal(["start 4", "complete 4", "start 5", "complete 5", "start 6", "complete 6"], reporter.Sequence);
        Assert.Equal(["Program Model", "Analysis", "Emit"], reporter.Events.Where(e => e.Kind == "start").Select(e => e.Title));
        Assert.Equal(["3 .cs files written", "bin/Probe.dll"], reporter.DetailsOf("6"));
        Assert.Equal(0, reporter.Summaries);
    }

    [Fact]
    public void ErrorsOnEntryReportNoStageAndBuildNothing()
    {
        diagnostics.Add(Error());

        var result = Pipeline().Execute(Input());

        Assert.False(result.Succeeded);
        Assert.Empty(reporter.Events);
        Assert.Empty(ran);
    }

    [Fact]
    public void NoModelCompletesStageFourWithNoConstructsAndStops()
    {
        var result = Pipeline(build: _ => Ran("build", new ModelBuild(null, _ => null))).Execute(Input());

        Assert.False(result.Succeeded);
        Assert.Equal(["start 4", "complete 4"], reporter.Sequence);
        Assert.Equal(["0 top-level constructs"], reporter.DetailsOf("4"));
        Assert.Equal(["build"], ran);
    }

    [Fact]
    public void BindingErrorsWithAModelAreStillAnalysedButNotEmitted()
    {
        var result = Pipeline(build: _ =>
        {
            diagnostics.Add(Error());
            return Ran("build", Built());
        }).Execute(Input());

        Assert.False(result.Succeeded);
        Assert.Equal(["start 4", "complete 4", "start 5", "complete 5"], reporter.Sequence);
        Assert.Equal(["build", "analyze"], ran);
    }

    [Fact]
    public void AnalysisErrorsStopBeforeEmission()
    {
        var result = Pipeline(analyze: (_, bag, _) =>
        {
            ran.Add("analyze");
            bag.Add(Error());
        }).Execute(Input());

        Assert.False(result.Succeeded);
        Assert.Equal(["start 4", "complete 4", "start 5", "complete 5"], reporter.Sequence);
        Assert.DoesNotContain("emit", ran);
    }

    [Fact]
    public void AResolutionErrorLetsStagesFourAndFiveRunAndStopsBeforeEmission()
    {
        resolution.Add(Error());

        var result = Pipeline().Execute(Input());

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryOutputArtifactPath);
        Assert.Equal(["start 4", "complete 4", "start 5", "complete 5"], reporter.Sequence);
        Assert.Equal(["build", "analyze"], ran);
    }

    [Fact]
    public void AResolutionWarningChangesNothing()
    {
        resolution.Add(Error() with { Severity = DiagnosticSeverity.Warning });

        var result = Pipeline().Execute(Input());

        Assert.True(result.Succeeded);
        Assert.Equal(["build", "analyze", "emit"], ran);
    }

    [Fact]
    public void AFailedEmissionStillCompletesStageSixWithWhatItWroteAndNoArtifact()
    {
        var failed = new CompilerResult(false, null, []);

        var result = Pipeline(emit: (_, _, _) => new Emission(failed, 5)).Execute(Input());

        Assert.Same(failed, result);
        Assert.Equal(["start 6", "complete 6"], reporter.Sequence.TakeLast(2));
        Assert.Equal(["5 .cs files written"], reporter.DetailsOf("6"));
    }

    [Fact]
    public void StageFiveReportsWhatTheAnalyzersRecorded()
    {
        _ = Pipeline(analyze: (_, _, timings) =>
        {
            timings.RecordScope(AnalysisScopeKind.Program);
            timings.Record(new Probe(), TimeSpan.FromSeconds(1));
        }).Execute(Input());

        var details = reporter.DetailsOf("5");
        Assert.Equal("1 analyzers over 1 scopes: 1 program, 0 clone conversions, 0 cloned types", details[0]);
        Assert.Equal("probe (Probe)  1.00s", details[1]);
        Assert.StartsWith("outside analyzers  ", details[2]);
        Assert.Equal(3, details.Count);
    }

    [Fact]
    public void TheModelOutlivesEmission()
    {
        var lifetime = new Lifetime();
        var disposedAtEmission = true;

        _ = Pipeline(
            build: _ => new ModelBuild(new ProgramModelClass(), _ => null, lifetime),
            emit: (_, _, _) =>
            {
                disposedAtEmission = lifetime.Disposed;
                return new Emission(new CompilerResult(true, null, []), 0);
            }).Execute(Input());

        Assert.False(disposedAtEmission);
        Assert.True(lifetime.Disposed);
    }

    [Fact]
    public void AStageThatThrowsIsNotReportedComplete()
    {
        var pipeline = Pipeline(build: _ => throw new InvalidOperationException("binder fault"));

        _ = Assert.Throws<InvalidOperationException>(() => pipeline.Execute(Input()));
        Assert.Equal(["start 4"], reporter.Sequence);
    }

    private StandardPipeline Pipeline(
        Func<PipelineInput, ModelBuild>? build = null,
        Action<IProgramModel, DiagnosticBag, AnalysisTimings>? analyze = null,
        Func<IProgramModel, Func<string, Type?>, PipelineInput, Emission>? emit = null)
    {
        return new StandardPipeline(
            reporter,
            build ?? (_ => Ran("build", Built())),
            analyze ?? ((_, _, _) => ran.Add("analyze")),
            emit ?? ((_, _, _) => Ran("emit", new Emission(new CompilerResult(true, null, []), 0))));
    }

    private T Ran<T>(string stage, T outcome)
    {
        ran.Add(stage);
        return outcome;
    }

    private static ModelBuild Built()
    {
        return new ModelBuild(new ProgramModelClass(), _ => null);
    }

    private PipelineInput Input()
    {
        var options = new CompilerOptions("probe", [], new BackendId("csharp"), "out", "bin", "Probe", "Probe");
        return new PipelineInput(new MistCompilation([]), [], [], options, diagnostics, resolution);
    }

    private static Diagnostic Error()
    {
        return new Diagnostic(
            new DiagnosticId("MIST_PROBE"), DiagnosticSeverity.Error, PipelineStage.Driver, "probe", default, "probe.mist", null);
    }

    private sealed class Probe : IProgramAnalyzer
    {
        public string Id => "probe";

        public void Analyze(AnalysisContext context)
        {
        }
    }

    private sealed class Lifetime : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}