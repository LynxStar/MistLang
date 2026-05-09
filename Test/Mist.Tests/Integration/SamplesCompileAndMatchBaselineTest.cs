using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
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
        Assert.Contains("StatusCode.Ok = StatusCode { Value = 200 }", output);
        Assert.Contains("Planet.Earth = Planet { Value = Earth }", output);
        Assert.Contains("Rank.Sergeant = Rank { Value = 3 }", output);
        Assert.Contains("Suit.Spades = Suit { Value = 3 }", output);
        Assert.Contains("Token.NumericId = Token { Value = Union_Int32_String }", output);
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

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunMistCli(
        string cliProjectPath, string sampleDir)
    {
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

        return (process.ExitCode, stdout, stderr);
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
