using System.Diagnostics;

namespace Mist.Tests.Integration;

/// <summary>
/// Regression guard that compiles and runs the AllFeatures sample project,
/// then diffs the captured stdout against the stored baseline. AllFeatures
/// exercises every feature sample via project references, so this single
/// test covers the entire sample suite.
/// </summary>
public sealed class SamplesCompileAndMatchBaselineTest
{
    [Fact]
    public async Task AllFeatures_CompilesRunsAndMatchesBaseline()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "AllFeatures");
        var baselineFile = Path.Combine(repoRoot, "samples", "__baselines__", "AllFeatures.out");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(baselineFile), $"Baseline file not found: {baselineFile}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        // Invoke the Mist CLI to compile and run the sample with --quiet
        // so only the program's stdout is captured (no progress output).
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{cliProjectPath}\" -v q -- run \"{sampleDir}\" --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.True(
            process.ExitCode == 0,
            $"AllFeatures failed to compile/run (exit code {process.ExitCode}).\nStderr:\n{stderr}");

        var expected = NormalizeLineEndings(await File.ReadAllTextAsync(baselineFile));
        var actual = NormalizeLineEndings(stdout);

        Assert.Equal(expected, actual);
    }

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}
