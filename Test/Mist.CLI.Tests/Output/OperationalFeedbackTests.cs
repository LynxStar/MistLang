using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "2")]
public sealed class OperationalFeedbackTests
{
    [Fact]
    public void AParseFailureIsAnOccurrenceInTheUsageDocument()
    {
        using var error = new StringWriter();
        var controls = new DiagnosticControls(DiagnosticFormat.Json);
        var parsed = new CliParseResult(null,
            [new CliParseError(CommandDiagnostics.UnknownFlag, [new NameValue("--missing")])], controls);

        var exit = CliHost.Run([], (_, _) => throw new InvalidOperationException("must not dispatch"), error, _ => parsed);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(error.ToString());
        Assert.Equal("usageError", document.RootElement.GetProperty("outcome").GetString());
        var finding = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(CommandDiagnostics.UnknownFlag.Number.Value, finding.GetProperty("code").GetString());
        Assert.Equal("--missing", finding.GetProperty("arguments").GetProperty("flag").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpectedLaunchFailuresReturnOneAndUseTheOutputOwner(bool throws)
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error);

        var exit = ChildProcessRunner.Run(new ProcessStartInfo("dotnet"), output,
            _ => throws ? throw new Win32Exception("cannot launch") : null);
        output.Complete(false);

        Assert.Equal(1, exit);
        var expected = throws ? CommandDiagnostics.ProcessLaunchFailed : CommandDiagnostics.ProcessStartFailed;
        Assert.Contains(expected.Number.Value, error.ToString());
        Assert.Equal(1, output.Counts.Errors);
        _ = Assert.Single(error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ALateExpectedLaunchFailureKeepsOneDocumentAndWritesOneTextIox()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json);
        output.Complete(true);

        var exit = ChildProcessRunner.Run(new ProcessStartInfo("dotnet"), output, _ => null);
        output.Complete(false);

        var lines = error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1, exit);
        Assert.Equal(2, lines.Length);
        using var document = JsonDocument.Parse(lines[0]);
        Assert.Equal("succeeded", document.RootElement.GetProperty("outcome").GetString());
        Assert.StartsWith($"error {CommandDiagnostics.ProcessStartFailed.Number.Value}:", lines[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreCompletionWarningsAreCountedAndPromotionPreventsTheChild(bool promoted)
    {
        using var progress = new StringWriter();
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error);
        output.Operation.Policy = DiagnosticPolicy.Default with { PromoteAllWarnings = promoted };
        var launched = false;

        var exit = new CompileFeedback(new ConsoleProgressReporter(progress), output).Run(
            _ => new CompilerResult(true, "app.dll", []), Stopwatch.StartNew(), _ =>
            {
                launched = true;
                return 0;
            }, result =>
            {
                var bag = new DiagnosticBag(output.Operation);
                bag.ReportTyped(DriverDiagnostics.TestFrameworkPackageMissing, new ProjectLocation("mist.toml", null));
                return result with { Diagnostics = result.Diagnostics.AddRange(bag.Admitted) };
            });

        Assert.Equal(promoted ? 1 : 0, exit);
        Assert.Equal(!promoted, launched);
        Assert.Contains(promoted ? "Build failed  1 error(s), 0 warning(s)" : "Build succeeded  0 error(s), 1 warning(s)", progress.ToString());
        Assert.Contains(DriverDiagnostics.TestFrameworkPackageMissing.Number.Value, error.ToString());
    }

    [Fact]
    public void AMissingArtifactRefusesTheChildBeforeCompletingTheDocument()
    {
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error, DiagnosticFormat.Json);
        var launched = false;

        var exit = new CompileFeedback(NullProgressReporter.Instance, output).Run(
            _ => new CompilerResult(true, null, []), Stopwatch.StartNew(), _ =>
            {
                launched = true;
                return 0;
            }, result => CommandFeedback.RequireArtifact(result, output));

        Assert.Equal(1, exit);
        Assert.False(launched);
        using var document = JsonDocument.Parse(error.ToString());
        Assert.Equal("failed", document.RootElement.GetProperty("outcome").GetString());
        var finding = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(CommandDiagnostics.ExecutableArtifactMissing.Number.Value, finding.GetProperty("code").GetString());
    }
}