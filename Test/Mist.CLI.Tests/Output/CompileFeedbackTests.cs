using System.Diagnostics;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Output;

/// <summary>
/// The compile commands' feedback chain over supplied outcomes: a compile delegate stands in for the
/// driver, delivering what Stage 1.5 would and returning the result it would, so no stage runs.
/// </summary>
[Trait("Tier", "2")]
public sealed class CompileFeedbackTests
{
    private static readonly Diagnostic PackageA = Error("MIST0036", "Package 'A' version 'x' could not be found.");
    private static readonly Diagnostic PackageB = Error("MIST0036", "Package 'B' version 'y' could not be found.");
    private static readonly Diagnostic Framework = Error("MIST0035", "Framework reference 'C' is not a known shared framework.");
    private static readonly Diagnostic Source = Error("MIST0298", "Type 'Widget' could not be resolved.", "Program.mist");

    [Fact]
    public void ADeliveredErrorIsWrittenWhenItArrives_AheadOfTheStagesAfterIt_AndNeverAgain()
    {
        using var console = new StringWriter();
        var progress = new ConsoleProgressReporter(console);
        var launched = false;

        var exit = new CompileFeedback(progress, console).Run(
            listener =>
            {
                listener.Receive(PackageA);
                progress.ReportStageStart("2", "Lexing");
                return Failed(PackageA, Source);
            },
            Stopwatch.StartNew(),
            _ => Launch(ref launched));

        var lines = Lines(console);
        Assert.Equal(4, lines.Count);
        Assert.Equal(DiagnosticFormatter.Format(PackageA), lines[0]);
        Assert.Equal("Stage 2 - Lexing", lines[1]);
        Assert.StartsWith("Build failed  2 error(s), 0 warning(s)", lines[2]);
        Assert.Equal(DiagnosticFormatter.Format(Source), lines[3]);
        Assert.Equal(1, exit);
        Assert.False(launched);
    }

    [Fact]
    public void DependenciesFailingWithOneIdAreEachWrittenOnce_AndProgressAndDiagnosticsKeepToTheirStreams()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var launched = false;

        var exit = new CompileFeedback(new ConsoleProgressReporter(output), error).Run(
            listener =>
            {
                listener.Receive(PackageA);
                listener.Receive(PackageB);
                listener.Receive(Framework);
                return Failed(PackageA, PackageB, Framework, Source);
            },
            Stopwatch.StartNew(),
            _ => Launch(ref launched));

        Assert.Equal(Formatted(PackageA, PackageB, Framework, Source), Lines(error));
        var summary = Assert.Single(Lines(output));
        Assert.StartsWith("Build failed  4 error(s), 0 warning(s)", summary);
        Assert.Equal(1, exit);
        Assert.False(launched);
    }

    [Fact]
    public void AnEqualDiagnosticThatWasNotTheDeliveredInstanceIsStillWritten()
    {
        using var error = new StringWriter();
        var copy = PackageA with { };

        _ = new CompileFeedback(NullProgressReporter.Instance, error).Run(
            listener =>
            {
                listener.Receive(PackageA);
                return Failed(PackageA, copy);
            },
            Stopwatch.StartNew(),
            _ => 0);

        Assert.Equal(Formatted(PackageA, copy), Lines(error));
    }

    [Fact]
    public void TwoEqualDiagnosticsDeliveredAreEachWrittenOnce()
    {
        using var error = new StringWriter();
        var copy = PackageA with { };

        _ = new CompileFeedback(NullProgressReporter.Instance, error).Run(
            listener =>
            {
                listener.Receive(PackageA);
                listener.Receive(copy);
                return Failed(PackageA, copy);
            },
            Stopwatch.StartNew(),
            _ => 0);

        Assert.Equal(Formatted(PackageA, copy), Lines(error));
    }

    [Fact]
    public void QuietProgressHidesTheSummaryAndNoDiagnostic()
    {
        using var error = new StringWriter();
        var launched = false;

        var exit = new CompileFeedback(NullProgressReporter.Instance, error).Run(
            listener =>
            {
                listener.Receive(PackageA);
                return Failed(PackageA, Source);
            },
            Stopwatch.StartNew(),
            _ => Launch(ref launched));

        Assert.Equal(Formatted(PackageA, Source), Lines(error));
        Assert.Equal(1, exit);
        Assert.False(launched);
    }

    [Fact]
    public void ASuccessWithNoDependenciesWritesItsWarningOnceAndHandsTheResultOn()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var warning = Warning("MIST0294", "Namespace 'Gone' is not defined.");
        var built = new CompilerResult(true, "bin/Probe.dll", [warning]);
        CompilerResult? handedOn = null;

        var exit = new CompileFeedback(new ConsoleProgressReporter(output), error).Run(
            _ => built,
            Stopwatch.StartNew(),
            result =>
            {
                handedOn = result;
                return 7;
            });

        Assert.Equal(Formatted(warning), Lines(error));
        Assert.StartsWith("Build succeeded  0 error(s), 1 warning(s)", Assert.Single(Lines(output)));
        Assert.Same(built, handedOn);
        Assert.Equal(7, exit);
    }

    [Fact]
    public void AWarningDeliveredDuringASucceededBuildIsNotRepeated()
    {
        using var error = new StringWriter();
        var warning = Warning(PackageA.Number.Value, PackageA.Message, PackageA.Location.Path ?? "");

        var exit = new CompileFeedback(NullProgressReporter.Instance, error).Run(
            listener =>
            {
                listener.Receive(warning);
                return new CompilerResult(true, "bin/Probe.dll", [warning]);
            },
            Stopwatch.StartNew(),
            _ => 0);

        Assert.Equal(Formatted(warning), Lines(error));
        Assert.Equal(0, exit);
    }

    [Fact]
    public void AFailureBeforeAnythingWasDeliveredIsWrittenInFull()
    {
        using var error = new StringWriter();
        var configuration = Error("TEST_TOML_PARSE", "mist.toml could not be parsed.", "mist.toml");
        var launched = false;

        var exit = new CompileFeedback(NullProgressReporter.Instance, error).Run(
            _ => Failed(configuration),
            Stopwatch.StartNew(),
            _ => Launch(ref launched));

        Assert.Equal(Formatted(configuration), Lines(error));
        Assert.Equal(1, exit);
        Assert.False(launched);
    }

    [Fact]
    public void EachRunWritesWhatItsOwnCompileReported_Once()
    {
        using var error = new StringWriter();
        var feedback = new CompileFeedback(NullProgressReporter.Instance, error);

        for(var run = 0; run < 2; run++)
        {
            var failure = PackageA with { };
            _ = feedback.Run(
                listener =>
                {
                    listener.Receive(failure);
                    return Failed(failure);
                },
                Stopwatch.StartNew(),
                _ => 0);
        }

        Assert.Equal(Formatted(PackageA, PackageA), Lines(error));
    }

    private static int Launch(ref bool launched)
    {
        launched = true;
        return 0;
    }

    private static CompilerResult Failed(params Diagnostic[] diagnostics)
    {
        return new CompilerResult(false, null, [.. diagnostics]);
    }

    private static List<string> Lines(StringWriter writer)
    {
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static List<string> Formatted(params Diagnostic[] diagnostics)
    {
        return diagnostics.Select(DiagnosticFormatter.Format).ToList();
    }

    private static Diagnostic Error(string id, string message, string path = "")
    {
        return DiagnosticFixtures.Occurrence(id, DiagnosticSeverity.Error, PipelineStage.Driver, message, default, path, null);
    }

    private static Diagnostic Warning(string id, string message, string path = "")
    {
        return DiagnosticFixtures.Occurrence(id, DiagnosticSeverity.Warning, PipelineStage.Driver, message, default, path, null);
    }
}