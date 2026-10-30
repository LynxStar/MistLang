using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Driver;

/// <summary>
/// Design Property 6: a promoted warning stops the pipeline at the same gate an error does, because
/// every gate reads the effective severity the policy settled at admission.
/// </summary>
[Trait("Tier", "2")]
public sealed class StandardPipelinePolicyGateTests
{
    private static readonly MistDiagnostic Warning = DiagnosticFixtures.Descriptor("FIXTURE_GATE", "probe", DiagnosticSeverity.Warning, policy: PolicyClass.Promotable);

    private readonly List<string> ran = [];

    [Theory]
    [InlineData("entry")]
    [InlineData("analysis")]
    [InlineData("resolution")]
    public void APromotedWarningStopsWhereAnErrorStops(string gate)
    {
        var promoted = Run(gate, DiagnosticPolicy.Default with { PromoteAllWarnings = true });
        var unpromoted = Run(gate, DiagnosticPolicy.Default);

        Assert.False(promoted.Result.Succeeded);
        Assert.True(unpromoted.Result.Succeeded);
        Assert.Equal(gate == "entry" ? [] : ["build", "analyze"], promoted.Ran);
        Assert.Equal(["build", "analyze", "emit"], unpromoted.Ran);
    }

    private (CompilerResult Result, List<string> Ran) Run(string gate, DiagnosticPolicy policy)
    {
        ran.Clear();
        var operation = new DiagnosticOperation { Policy = policy };
        var diagnostics = new DiagnosticBag(operation);
        var resolution = new DiagnosticBag(operation);

        if(gate == "entry")
        {
            diagnostics.ReportAt(Warning, new Absent(AbsenceReason.Unavailable));
        }

        if(gate == "resolution")
        {
            resolution.ReportAt(Warning, new Absent(AbsenceReason.Unavailable));
        }

        var pipeline = new StandardPipeline(
            new RecordingProgressReporter(),
            _ => Ran("build", new ModelBuild(new ProgramModelClass(), _ => null)),
            (_, bag, _) =>
            {
                ran.Add("analyze");
                if(gate == "analysis")
                {
                    bag.ReportAt(Warning, new Absent(AbsenceReason.Unavailable));
                }
            },
            (_, _, _) => Ran("emit", new Emission(new CompilerResult(true, null, []), 0)));

        var options = new CompilerOptions("probe", [], new BackendId("csharp"), "out", "bin", "Probe", "Probe");
        var result = pipeline.Execute(new PipelineInput(new MistCompilation([]), [], options, diagnostics, resolution));

        return (result, [.. ran]);
    }

    private T Ran<T>(string stage, T outcome)
    {
        ran.Add(stage);
        return outcome;
    }
}