using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mist.Tests.Integration;

/// <summary>
/// Regression guard that compiles and runs sample projects via the Mist CLI,
/// then diffs the captured stdout against stored baselines. AllFeaturesV2
/// exercises every feature sample via project references, so that single
/// test covers the entire sample suite. Individual sample tests verify
/// standalone compilation and expected output for specific features.
/// </summary>
public sealed class SamplesCompileAndMatchBaselineTest : IDisposable
{
    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "MistSamples_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch(IOException) { }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The one Tier 6 full-system test. It names no pipeline, so it holds the CLI's default.
    /// <para>
    /// <c>samples/AllFeaturesV2</c> IS the record of which samples work: it references each one
    /// and calls its <c>RunDemo()</c>. Banking a sample means adding it there and extending
    /// <c>samples/__baselines__/AllFeaturesV2.out</c>. Because a removed sample shrinks the
    /// program's output, this baseline diff makes recorded progress impossible to lose silently.
    /// </para>
    /// <para>
    /// See <c>.claude/skills/testing-philosophy/SKILL.md</c>, Tier 6.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Tier", "6")]
    public async Task AllFeaturesV2_CompilesRunsAndMatchesBaseline()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "AllFeaturesV2");
        var baselineFile = Path.Combine(repoRoot, "samples", "__baselines__", "AllFeaturesV2.out");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(baselineFile), $"Baseline file not found: {baselineFile}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        var (exitCode, stdout, stderr) = await RunMistCli(cliProjectPath, sampleDir);

        Assert.True(
            exitCode == 0,
            $"AllFeaturesV2 failed to compile/run (exit code {exitCode}).\nStderr:\n{stderr}");

        // THE FALSE-POSITIVE HALF OF THE ANALYSIS LEDGER. A Stage 5 rule that fires on a working program
        // may only WARN, which leaves the exit code clean and stdout untouched -- so without this, a rule
        // warning on every banked sample would pass. samples/AGENTS.md already holds every sample to zero
        // warnings; this is where that is enforced.
        var warnings = stderr.Split('\n').Where(line => Regex.IsMatch(line, @": warning [A-Z0-9_]+:")).ToList();
        Assert.True(
            warnings.Count == 0,
            $"AllFeaturesV2 compiled with warnings:\n{string.Join("\n", warnings)}");

        var expected = NormalizeLineEndings(await File.ReadAllTextAsync(baselineFile));
        var actual = NormalizeLineEndings(stdout);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Tier 5, and the one sample the orchestrator cannot hold: Testing is a library
    /// with no entry point, so it has no RunDemo to call and no stdout to diff, and its verdict
    /// comes from the external test runner. The runner's own tally is the assertion, and the
    /// absence of C# warnings is asserted with it because MSBuild is the only consumer that sees
    /// them -- the in-process compile does not treat a generated file as generated.
    /// </summary>
    [Fact]
    [Trait("Tier", "5")]
    public async Task Testing_RunsUnderTheTestCommand()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "Testing");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        var (exitCode, stdout, stderr) = await RunMistCli(cliProjectPath, sampleDir, command: "test");

        Assert.True(
            exitCode == 0,
            $"Testing failed under 'mist test' (exit code {exitCode}).\nStdout:\n{stdout}\nStderr:\n{stderr}");
        Assert.Matches(@"Passed:\s+5\b", stdout);
        Assert.Matches(@"Failed:\s+0\b", stdout);
        Assert.DoesNotContain("warning CS", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the EnumReadonlyRestriction sample compiles and runs
    /// without errors, producing the expected output demonstrating all
    /// readonly-base enum patterns (primitive, struct, class, name-only,
    /// union, member access, implicit conversion, pattern matching).
    /// </summary>
    [Fact]
    [Trait("Tier", "5")]
    public async Task EnumReadonlyRestriction_CompilesAndProducesExpectedOutput()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "EnumReadonlyRestriction");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        var (exitCode, stdout, stderr) = await RunMistCli(cliProjectPath, sampleDir);

        Assert.True(
            exitCode == 0,
            $"EnumReadonlyRestriction failed to compile/run (exit code {exitCode}).\nStderr:\n{stderr}");

        var output = NormalizeLineEndings(stdout);

        // Verify the sample header is present
        Assert.Contains("=== Enum Readonly Restriction Sample ===", output);

        // Verify each demonstrated pattern section is present
        Assert.Contains("--- Enum with int base type ---", output);
        Assert.Contains("--- Enum with string base type ---", output);
        Assert.Contains("--- Enum with byte base type ---", output);
        Assert.Contains("--- Enum with readonly struct base type ---", output);
        Assert.Contains("--- Enum with readonly class base type ---", output);
        Assert.Contains("--- Name-only enum (inferred byte) ---", output);
        Assert.Contains("--- Enum with union of readonly types ---", output);
        Assert.Contains("--- Member access on readonly-base enums ---", output);
        Assert.Contains("--- Implicit conversion to underlying type ---", output);
        Assert.Contains("--- Pattern matching with readonly-base enums ---", output);

        // Verify specific output values for key patterns
        Assert.Contains("StatusCode.Ok = Ok", output);
        Assert.Contains("Planet.Earth = Earth", output);
        Assert.Contains("Rank.Sergeant = Sergeant", output);
        Assert.Contains("Suit.Spades = Spades", output);
        Assert.Contains("Token.NumericId = NumericId", output);
        Assert.Contains("Palette.Red.Describe() = Color(255, 0, 0)", output);
        Assert.Contains("StatusCode.Ok as int = 200", output);
        Assert.Contains("Planet.Earth as string = Earth", output);
        Assert.Contains("code is StatusCode.NotFound = True", output);
        Assert.Contains("pal is Palette.Blue = True", output);
    }

    /// <summary>
    /// Runs in place rather than through the helper's scratch output: the facade in the sample's own
    /// App/bin is the claim, and the bridge writes Lib/bin in the sample whatever the output flags
    /// say. MistCommand sends the bridge's nested `mist build` to the compiler under test instead of
    /// the `mist` on PATH. This is the one test that runs MSBuild.
    /// </summary>
    [Fact]
    [Trait("Tier", "5")]
    public async Task CSharpReferences_RunsFromACleanTree()
    {
        var repoRoot = FindRepoRoot();
        var sampleRoot = Path.Combine(repoRoot, "samples", "CSharpReferences");
        var appDir = Path.Combine(sampleRoot, "App");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(appDir), $"Sample directory not found: {appDir}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        string[] buildArtifacts = ["App/bin", "App/out", "Lib/bin", "Lib/out", "Bridge/bin", "Bridge/obj"];
        foreach(var relative in buildArtifacts)
        {
            var path = Path.Combine(sampleRoot, relative);
            if(Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        var environment = new Dictionary<string, string>
        {
            ["MistCommand"] = $"dotnet run --project \"{cliProjectPath}\" --no-build -v q --",
        };

        var (exitCode, stdout, stderr) = await RunMistCli(
            cliProjectPath, appDir, environment: environment, redirectOutput: false);

        Assert.True(
            exitCode == 0,
            $"CSharpReferences failed to compile/run (exit code {exitCode}).\nStdout:\n{stdout}\nStderr:\n{stderr}");
        Assert.Contains("bridge:widget-1", NormalizeLineEndings(stdout), StringComparison.Ordinal);
        Assert.True(
            File.Exists(Path.Combine(appDir, "bin", "Lib.dll")),
            $"The Lib facade was not written to App/bin.\nStdout:\n{stdout}");
        Assert.DoesNotContain("warning CS", stdout, StringComparison.Ordinal);

        string[] bridgeIdentifiers = ["osx-arm64", "osx-x64", "linux-x64", "linux-arm64", "win-x64", "win-arm64"];
        string[] bridgeFileNames = ["libbridge-native.dylib", "libbridge-native.so", "bridge-native.dll"];
        var hostIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        Assert.True(
            bridgeIdentifiers.Contains(hostIdentifier),
            $"Bridge ships no fake native file for the host identifier '{hostIdentifier}'.");

        var hostFileName = hostIdentifier.Split('-')[0] switch
        {
            "osx" => "libbridge-native.dylib",
            "linux" => "libbridge-native.so",
            _ => "bridge-native.dll",
        };
        var binDirectory = Path.Combine(appDir, "bin");
        foreach(var fileName in bridgeFileNames)
        {
            var found = Directory.GetFiles(binDirectory, fileName, SearchOption.AllDirectories);
            if(fileName == hostFileName)
            {
                var deployed = Assert.Single(found);
                Assert.Equal(Path.Combine(binDirectory, fileName), deployed);
            }
            else
            {
                Assert.Empty(found);
            }
        }

        Assert.Equal(
            $"bridge-native {hostIdentifier}",
            (await File.ReadAllTextAsync(Path.Combine(binDirectory, hostFileName))).Trim());
    }

    /// <summary>
    /// Runs a sample through the Mist CLI on its default pipeline.
    /// <para>
    /// --no-build IS LOAD-BEARING, not a speed-up. Without it this is a SECOND MSBuild inside the one
    /// running the suite, and it re-runs Mist.CLI's Content copy of Source/Standard into the very
    /// directory the `mist` it launches then reads through AppContext.BaseDirectory. A test that
    /// rewrites the standard library out from under a concurrently starting compiler is the
    /// suite-wide flake recorded in Carbonek as ARCH-135/ARCH-136 -- whole classes failing together with
    /// MIST0293 for `Standard`, and recovering on the next run. The copy is
    /// PreserveNewest, so it is a no-op until a Source/Standard file's timestamp moves, which is why
    /// the flake clusters in sessions that touch the standard library.
    /// </para>
    /// <para>
    /// What guarantees there is something to run is the ProjectReference on Mist.CLI in
    /// Mist.Tests.csproj, carried for its build ORDER alone. Neither half works without the other.
    /// </para>
    /// </summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunMistCli(
        string cliProjectPath,
        string sampleDir,
        string command = "run",
        IReadOnlyDictionary<string, string>? environment = null,
        bool redirectOutput = true)
    {
        var outputFlags = redirectOutput
            ? $" --output \"{Path.Combine(scratchDir, "out")}\" --bin \"{Path.Combine(scratchDir, "bin")}\""
            : "";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{cliProjectPath}\" --no-build -v q -- {command} \"{sampleDir}\" --quiet{outputFlags}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            }
        };

        foreach(var (name, value) in environment ?? new Dictionary<string, string>())
        {
            process.StartInfo.Environment[name] = value;
        }

        _ = process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}