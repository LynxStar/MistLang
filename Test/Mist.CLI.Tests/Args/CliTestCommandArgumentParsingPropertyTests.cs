using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

public class CliTestCommandArgumentParsingPropertyTests
{
#pragma warning disable CA1034

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

    /// <summary>
    /// Never sets both --quiet and --verbose, which the parser rejects together.
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

    [Property(Arbitrary = new[] { typeof(CliTestCommandArgumentParsingPropertyTests) }, MaxTest = 100)]
    public bool Parse_TestCommand_ExtractsAllFlagsCorrectly(TestCommandScenario scenario)
    {
        var args = scenario.ToArgs();
        var result = CliArgumentParser.Parse(args);

        if(result.Errors.Count > 0 || result.Arguments is null)
        {
            return false;
        }

        var parsed = result.Arguments;

        if(parsed.Command != CliCommand.Test)
        {
            return false;
        }

        var expectedBackend = scenario.BackendId ?? "csharp";
        if(parsed.BackendId != expectedBackend)
        {
            return false;
        }

        if(scenario.OutputDir is not null && parsed.OutputDirectory != scenario.OutputDir)
        {
            return false;
        }

        if(scenario.BinDir is not null && parsed.BinDirectory != scenario.BinDir)
        {
            return false;
        }

        if(parsed.Quiet != scenario.Quiet)
        {
            return false;
        }

        if(parsed.Verbose != scenario.Verbose)
        {
            return false;
        }

        return true;
    }

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

        if(scenario.OutputDir is null && parsed.OutputDirectory != Path.Combine(cwd, "out"))
        {
            return false;
        }

        if(scenario.BinDir is null && parsed.BinDirectory != Path.Combine(cwd, "bin"))
        {
            return false;
        }

        return true;
    }
}