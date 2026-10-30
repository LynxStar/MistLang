using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Mist.Driver;

internal sealed record DotnetInvocation(
    string Arguments,
    string WorkingDirectory,
    ImmutableDictionary<string, string> Environment,
    TimeSpan Timeout)
{
    public bool CleanEnvironment { get; init; }

    public ImmutableArray<string> ArgumentList { get; init; }
}

internal sealed record DotnetOutcome(int ExitCode, string Stdout, string Stderr, bool TimedOut);

internal static class DotnetProcess
{
    public static DotnetOutcome Run(DotnetInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = invocation.Arguments,
            WorkingDirectory = invocation.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if(!invocation.ArgumentList.IsDefault)
        {
            psi.Arguments = "";
            foreach(var argument in invocation.ArgumentList)
            {
                psi.ArgumentList.Add(argument);
            }
        }

        if(invocation.CleanEnvironment)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            var runtimeRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            psi.Environment.Clear();
            if(path is not null)
            {
                psi.Environment["PATH"] = path;
            }

            if(runtimeRoot is not null)
            {
                psi.Environment["DOTNET_ROOT"] = runtimeRoot;
            }
        }

        foreach(var (name, value) in invocation.Environment)
        {
            psi.Environment[name] = value;
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if(e.Data is not null)
            {
                _ = stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if(e.Data is not null)
            {
                _ = stderr.AppendLine(e.Data);
            }
        };

        _ = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if(!process.WaitForExit(invocation.Timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }

            return new DotnetOutcome(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        process.WaitForExit();
        return new DotnetOutcome(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
    }
}