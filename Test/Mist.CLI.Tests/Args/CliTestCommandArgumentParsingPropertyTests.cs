// Feature: attributes-and-test-command, Property 3: CLI Test Command Argument Parsing
// **Validates: Requirements 5.1, 5.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

/// <summary>
/// Property-based tests verifying that for any valid combination of CLI arguments
/// where the first positional argument is "test" and remaining arguments are drawn
/// from the same flag set accepted by build (--backend, --output, --bin, --quiet,
/// --verbose), the argument parser produces a CliArguments with Command == Test
/// and all flags correctly populated.
/// </summary>
public class CliTestCommandArgumentParsingPropertyTests
{
    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a randomly generated set of flags for the test command.
    /// --quiet and --verbose are mutually exclusive, so only one may be true.
    /// </summary>
    public record TestCommandScenario(
        string? BackendId,
        string? OutputDir,
        string? BinDir,
        bool Quiet,
        bool Verbose)
    {
        public override string ToString()
        {
            var parts = new List<string> { "test" };
            if(BackendId is not null)
            {
                parts.Add($"--backend {BackendId}");
            }

            if(OutputDir is not null)
            {
                parts.Add($"--output {OutputDir}");
            }

            if(BinDir is not null)
            {
                parts.Add($"--bin {BinDir}");
            }

            if(Quiet)
            {
                parts.Add("--quiet");
            }

            if(Verbose)
            {
                parts.Add("--verbose");
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Builds the args array as it would be passed to CliArgumentParser.Parse.
        /// </summary>
        public string[] ToArgs()
        {
            var args = new List<string> { "test" };
            if(BackendId is not null)
            {
                args.Add("--backend");
                args.Add(BackendId);
            }

            if(OutputDir is not null)
            {
                args.Add("--output");
                args.Add(OutputDir);
            }

            if(BinDir is not null)
            {
                args.Add("--bin");
                args.Add(BinDir);
            }

            if(Quiet)
            {
                args.Add("--quiet");
            }

            if(Verbose)
            {
                args.Add("--verbose");
            }

            return args.ToArray();
        }
    }

#pragma warning restore CA1034

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates valid test command scenarios with random subsets of flags.
    /// Ensures --quiet and --verbose are never both true (mutually exclusive).
    /// </summary>
    public static Arbitrary<TestCommandScenario> ArbitraryTestCommandScenario()
    {
        var backendIds = new[] { "csharp", "js", "wasm", "llvm", "custom-backend" };
        var outputDirs = new[] { "./out", "/tmp/output", "dist", "build/output" };
        var binDirs = new[] { "./bin", "/tmp/bin", "artifacts", "build/bin" };

        var gen = from includeBackend in Gen.Elements(true, false)
                  from backendId in Gen.Elements(backendIds)
                  from includeOutput in Gen.Elements(true, false)
                  from outputDir in Gen.Elements(outputDirs)
                  from includeBin in Gen.Elements(true, false)
                  from binDir in Gen.Elements(binDirs)
                  from verbosity in Gen.Elements("none", "quiet", "verbose")
                  select new TestCommandScenario(
                      BackendId: includeBackend ? backendId : null,
                      OutputDir: includeOutput ? outputDir : null,
                      BinDir: includeBin ? binDir : null,
                      Quiet: verbosity == "quiet",
                      Verbose: verbosity == "verbose");

        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any valid combination of CLI arguments with "test" as the
    /// command verb and a random subset of flags, the parser produces Command == Test
    /// and all flag values are correctly extracted.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CliTestCommandArgumentParsingPropertyTests) }, MaxTest = 100)]
    public bool Parse_TestCommand_ExtractsAllFlagsCorrectly(TestCommandScenario scenario)
    {
        var args = scenario.ToArgs();
        var result = CliArgumentParser.Parse(args);

        // Should parse without errors
        if(result.Errors.Count > 0 || result.Arguments is null)
        {
            return false;
        }

        var parsed = result.Arguments;

        // Command must be Test
        if(parsed.Command != CliCommand.Test)
        {
            return false;
        }

        // Backend must match (default is "csharp" when not specified)
        var expectedBackend = scenario.BackendId ?? "csharp";
        if(parsed.BackendId != expectedBackend)
        {
            return false;
        }

        // Output directory must match (when specified explicitly)
        if(scenario.OutputDir is not null && parsed.OutputDirectory != scenario.OutputDir)
        {
            return false;
        }

        // Bin directory must match (when specified explicitly)
        if(scenario.BinDir is not null && parsed.BinDirectory != scenario.BinDir)
        {
            return false;
        }

        // Quiet flag
        if(parsed.Quiet != scenario.Quiet)
        {
            return false;
        }

        // Verbose flag
        if(parsed.Verbose != scenario.Verbose)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Property 3 (default directory variant): When no --output or --bin flags are
    /// provided, the parser derives default directories from the project folder
    /// (current directory since no project folder positional arg is given).
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CliTestCommandArgumentParsingPropertyTests) }, MaxTest = 100)]
    public bool Parse_TestCommand_DefaultDirectories_DerivedFromCwd(TestCommandScenario scenario)
    {
        var args = scenario.ToArgs();
        var result = CliArgumentParser.Parse(args);

        if(result.Errors.Count > 0 || result.Arguments is null)
        {
            return false;
        }

        var parsed = result.Arguments;
        var cwd = Directory.GetCurrentDirectory();

        // When output is not specified, it should default to <cwd>/out
        if(scenario.OutputDir is null && parsed.OutputDirectory != Path.Combine(cwd, "out"))
        {
            return false;
        }

        // When bin is not specified, it should default to <cwd>/bin
        if(scenario.BinDir is null && parsed.BinDirectory != Path.Combine(cwd, "bin"))
        {
            return false;
        }

        return true;
    }
}