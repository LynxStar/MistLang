using System.Diagnostics;
using Mist.CLI.Output;

namespace Mist.CLI.Tests;

[Trait("Tier", "4")]
public sealed class ChildProcessRunnerSeamTests
{
    [Fact]
    public void ANonzeroChildExitCodeIsReturnedUnchangedWithoutADiagnostic()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", ["/c", "exit 42"])
            : new ProcessStartInfo("/bin/sh", ["-c", "exit 42"]);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        using var error = new StringWriter();
        var output = new DiagnosticOutput(error);

        var exit = ChildProcessRunner.Run(startInfo, output);
        output.Complete(true);

        Assert.Equal(42, exit);
        Assert.Equal(0, output.Counts.Errors);
        Assert.Equal("", error.ToString());
    }
}