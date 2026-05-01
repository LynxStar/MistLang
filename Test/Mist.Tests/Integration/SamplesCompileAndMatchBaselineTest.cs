using System.Diagnostics;

namespace Mist.Tests.Integration;

/// <summary>
/// Regression guard that compiles and runs every sample project that has a
/// baseline file in <c>samples/__baselines__/</c>, then diffs the captured
/// stdout against the stored baseline. A new test case is generated for each
/// <c>.out</c> file so that per-sample pass/fail is clearly visible.
///
/// Covers Requirements 11.1, 11.2, 11.3, 11.4, 11.6 and Property 12.6.
/// </summary>
public sealed class SamplesCompileAndMatchBaselineTest
{
    /// <summary>
    /// Discovers all <c>.out</c> baseline files under <c>samples/__baselines__/</c>.
    /// Each entry yields the sample directory name (baseline filename minus extension).
    /// </summary>
    public static IEnumerable<object[]> BaselineSamples()
    {
        var repoRoot = FindRepoRoot();
        var baselinesDir = Path.Combine(repoRoot, "samples", "__baselines__");

        if (!Directory.Exists(baselinesDir))
            yield break;

        foreach (var file in Directory.GetFiles(baselinesDir, "*.out").OrderBy(f => f, StringComparer.Ordinal))
        {
            var sampleName = Path.GetFileNameWithoutExtension(file);
            yield return new object[] { sampleName };
        }
    }

    [Theory]
    [MemberData(nameof(BaselineSamples))]
    public async Task Sample_CompilesRunsAndMatchesBaseline(string sampleName)
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", sampleName);
        var baselineFile = Path.Combine(repoRoot, "samples", "__baselines__", $"{sampleName}.out");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(baselineFile), $"Baseline file not found: {baselineFile}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        // Invoke the Mist CLI to compile and run the sample
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{cliProjectPath}\" -- run \"{sampleDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Assert the process exited successfully
        Assert.True(
            process.ExitCode == 0,
            $"Sample '{sampleName}' failed to compile/run (exit code {process.ExitCode}).\nStderr:\n{stderr}");

        // Read the baseline and compare (normalize line endings)
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
