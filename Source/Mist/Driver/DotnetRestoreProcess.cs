using System.Diagnostics;
using System.Text;

namespace Mist.Driver;

internal static class DotnetRestoreProcess
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(120);

    public static (int ExitCode, string Stdout, string Stderr) Run(
        string csprojPath, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{csprojPath}\" --verbosity quiet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if(e.Data is not null)
            {
                _ = stdoutBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if(e.Data is not null)
            {
                _ = stderrBuilder.AppendLine(e.Data);
            }
        };

        _ = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = process.WaitForExit((int)RestoreTimeout.TotalMilliseconds);

        if(!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }

            return (-1, stdoutBuilder.ToString(), "Restore timed out after 120 seconds.");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}