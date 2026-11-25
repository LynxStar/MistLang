using System.Collections.Immutable;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "4")]
public sealed class DotnetProcessTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(1);

    [Fact]
    public void ACompletedRun_ReportsExitCodeZero_AndTheVersionAsOneStdoutLine()
    {
        var outcome = DotnetProcess.Run(Invocation("--version", AppContext.BaseDirectory));

        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.TimedOut);
        AssertSingleVersionLine(outcome.Stdout);
        Assert.Equal("", outcome.Stderr);
    }

    [Fact]
    public void AnEnvironmentEntryReachesTheChild_AndItsStderrStaysApartFromStdout()
    {
        var environment = ImmutableDictionary<string, string>.Empty.Add("COREHOST_TRACE", "1");

        var outcome = DotnetProcess.Run(Invocation("--version", AppContext.BaseDirectory, environment));

        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.TimedOut);
        AssertSingleVersionLine(outcome.Stdout);
        Assert.StartsWith("Tracing enabled @ ", outcome.Stderr, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, outcome.Stderr, StringComparison.Ordinal);
        Assert.True(outcome.Stderr.Split(Environment.NewLine).Length > 2, outcome.Stderr);
    }

    [Fact]
    public void TheChildRunsInTheGivenWorkingDirectory()
    {
        var name = $"mist-dotnetprocess-{Guid.NewGuid():N}";
        var directory = Path.Combine(Path.GetTempPath(), name);
        _ = Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "global.json"),
                """{ "sdk": { "version": "0.0.1", "rollForward": "disable" } }""");

            var outcome = DotnetProcess.Run(Invocation("--version", directory));

            Assert.False(outcome.TimedOut);
            Assert.NotEqual(0, outcome.ExitCode);
            Assert.Contains(Path.Combine(name, "global.json"), outcome.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ANonzeroExitCodeIsReturnedUnchanged()
    {
        var outcome = DotnetProcess.Run(Invocation("mist-absent-command-probe", AppContext.BaseDirectory));

        Assert.Equal(1, outcome.ExitCode);
        Assert.False(outcome.TimedOut);
    }

    [Fact]
    public void AnElapsedTimeout_ReportsMinusOne_AndTimedOut()
    {
        // A zero timeout is checked immediately after the start, long before the host can resolve an SDK.
        var outcome = DotnetProcess.Run(Invocation("--version", AppContext.BaseDirectory) with { Timeout = TimeSpan.Zero });

        Assert.Equal(-1, outcome.ExitCode);
        Assert.True(outcome.TimedOut);
    }

    private static DotnetInvocation Invocation(
        string arguments, string workingDirectory, ImmutableDictionary<string, string>? environment = null)
    {
        return new DotnetInvocation(arguments, workingDirectory, environment ?? ImmutableDictionary<string, string>.Empty, Generous);
    }

    private static void AssertSingleVersionLine(string stdout)
    {
        Assert.EndsWith(Environment.NewLine, stdout, StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?\z", stdout[..^Environment.NewLine.Length]);
    }
}