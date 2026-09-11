using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Regression guard that compiles and runs sample projects via the Mist CLI,
/// then diffs the captured stdout against stored baselines. AllFeatures
/// exercises every feature sample via project references, so that single
/// test covers the entire sample suite. Individual sample tests verify
/// standalone compilation and expected output for specific features.
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
        var (exitCode, stdout, stderr) = await RunMistCli(cliProjectPath, sampleDir);

        Assert.True(
            exitCode == 0,
            $"AllFeatures failed to compile/run (exit code {exitCode}).\nStderr:\n{stderr}");

        var expected = NormalizeLineEndings(await File.ReadAllTextAsync(baselineFile));
        var actual = NormalizeLineEndings(stdout);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Tier 6 full-system test for the EXPERIMENTAL pipeline, the counterpart to
    /// <see cref="AllFeatures_CompilesRunsAndMatchesBaseline"/>. Exactly two Tier 6
    /// tests are permitted, one per pipeline.
    /// <para>
    /// <c>samples/AllFeaturesV2</c> IS the recorded v2 progression: it references
    /// exactly those samples that work on the experimental pipeline and calls each
    /// one's <c>RunDemo()</c>. Enabling a sample on v2 means adding it there and
    /// extending <c>samples/__baselines__/AllFeaturesV2.out</c>. Because a removed
    /// sample shrinks the program's output, this baseline diff makes recorded
    /// progress impossible to lose silently.
    /// </para>
    /// <para>
    /// The stored baseline was captured from the classic pipeline, which compiles and
    /// runs this sample correctly, so it is pipeline-independent: both pipelines are
    /// held to the same output. The two exceptions are LocalFunctions, which classic cannot
    /// parse, and OperatorOverloads, which classic cannot compile: their lines were captured
    /// from v2 and read against the program by hand.
    /// </para>
    /// <para>
    /// See <c>.kiro/steering/testing-philosophy.md</c>, Tier 6.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AllFeaturesV2_CompilesRunsAndMatchesBaseline()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "AllFeaturesV2");
        var baselineFile = Path.Combine(repoRoot, "samples", "__baselines__", "AllFeaturesV2.out");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(baselineFile), $"Baseline file not found: {baselineFile}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        var (exitCode, stdout, stderr) = await RunMistCli(cliProjectPath, sampleDir, "experimental");

        Assert.True(
            exitCode == 0,
            $"AllFeaturesV2 failed to compile/run on the experimental pipeline (exit code {exitCode}).\nStderr:\n{stderr}");

        // THE FALSE-POSITIVE HALF OF THE ANALYSIS LEDGER. A Stage 5 rule that fires on a working program
        // may only WARN, which leaves the exit code clean and stdout untouched -- so without this, a rule
        // warning on every banked sample would pass. samples/CLAUDE.md already holds every sample to zero
        // warnings; this is where that is enforced.
        var warnings = stderr.Split('\n').Where(line => Regex.IsMatch(line, @": warning [A-Z0-9_]+:")).ToList();
        Assert.True(
            warnings.Count == 0,
            $"AllFeaturesV2 compiled with warnings on the experimental pipeline:\n{string.Join("\n", warnings)}");

        var expected = NormalizeLineEndings(await File.ReadAllTextAsync(baselineFile));
        var actual = NormalizeLineEndings(stdout);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Tier 5, and the one sample the experimental orchestrator cannot hold: Testing is a library
    /// with no entry point, so it has no RunDemo to call and no stdout to diff, and its verdict
    /// comes from the external test runner. The runner's own tally is the assertion, and the
    /// absence of C# warnings is asserted with it because MSBuild is the only consumer that sees
    /// them -- the in-process compile does not treat a generated file as generated.
    /// </summary>
    [Fact]
    public async Task Testing_RunsUnderTheTestCommandOnTheExperimentalPipeline()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "Testing");
        var cliProjectPath = Path.Combine(repoRoot, "Source", "Mist.CLI", "Mist.CLI.csproj");

        Assert.True(Directory.Exists(sampleDir), $"Sample directory not found: {sampleDir}");
        Assert.True(File.Exists(cliProjectPath), $"CLI project not found: {cliProjectPath}");

        var (exitCode, stdout, stderr) = await RunMistCli(cliProjectPath, sampleDir, "experimental", command: "test");

        Assert.True(
            exitCode == 0,
            $"Testing failed under 'mist test' on the experimental pipeline (exit code {exitCode}).\nStdout:\n{stdout}\nStderr:\n{stderr}");
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
    /// Verifies that the existing Enums sample passes Mist compilation
    /// (parsing, semantic model, type checker) without errors after adding
    /// the <c>readonly</c> modifier to Vec2, Label, and Animal.
    /// This is a regression test ensuring the enum readonly restriction
    /// does not break existing enum patterns with user-defined base types.
    ///
    /// Note: The full end-to-end run (including C# emitter output) is blocked
    /// by a known emitter bug in __CreateDefault() for readonly structs with
    /// fields. This test verifies the language-level compilation is correct.
    /// </summary>
    [Fact]
    public void Enums_StillCompilesAfterReadonlyModifierAdditions()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "Enums");
        var sourceFiles = Directory.GetFiles(sampleDir, "*.mist");
        Assert.NotEmpty(sourceFiles);

        var driver = CreateRealDriver();
        var options = new CompilerOptions(
            ProjectFolder: sampleDir,
            SourcePaths: sourceFiles.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(sampleDir, "out"),
            BinDirectory: Path.Combine(sampleDir, "bin"),
            ProjectName: "Enums",
            NamespaceRoot: "Enums",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var result = driver.Compile(options);

        // Filter to only Mist-level errors (exclude MIST_CS_ERROR which is the
        // known __CreateDefault() emitter bug for readonly structs with fields).
        var mistErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                     && d.Id.Value != "MIST_CS_ERROR")
            .ToList();

        Assert.Empty(mistErrors);

        // Verify the type checker did not reject the readonly enum base types
        var enumReadonlyErrors = result.Diagnostics
            .Where(d => d.Id.Value is "MIST_TC_0102" or "MIST_TC_0103")
            .ToList();

        Assert.Empty(enumReadonlyErrors);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilerDriver CreateRealDriver()
    {
        return new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    /// <summary>
    /// Runs a sample through the Mist CLI. When <paramref name="pipeline"/> is null the
    /// CLI default (classic) is used; pass "experimental" to select the v2 pipeline.
    /// <para>
    /// --no-build IS LOAD-BEARING, not a speed-up. Without it this is a SECOND MSBuild inside the one
    /// running the suite, and it re-runs Mist.CLI's Content copy of Source/Standard into the very
    /// directory the `mist` it launches then reads through AppContext.BaseDirectory. A test that
    /// rewrites the standard library out from under a concurrently starting compiler is the
    /// suite-wide flake in backlog/flaky-tests.yaml -- whole classes failing together with
    /// MIST_UNDEFINED_NAMESPACE for `Standard`, and recovering on the next run. The copy is
    /// PreserveNewest, so it is a no-op until a Source/Standard file's timestamp moves, which is why
    /// the flake clusters in sessions that touch the standard library.
    /// </para>
    /// <para>
    /// What guarantees there is something to run is the ProjectReference on Mist.CLI in
    /// Mist.Tests.csproj, carried for its build ORDER alone. Neither half works without the other.
    /// </para>
    /// </summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunMistCli(
        string cliProjectPath, string sampleDir, string? pipeline = null, string command = "run")
    {
        var pipelineFlag = pipeline is null ? "" : $" --pipeline {pipeline}";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{cliProjectPath}\" --no-build -v q -- {command} \"{sampleDir}\" --quiet{pipelineFlag}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            }
        };

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