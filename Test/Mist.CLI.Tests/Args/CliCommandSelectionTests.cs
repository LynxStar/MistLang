using Mist.CLI.Args;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Args;

/// <summary>
/// The expectations come from <c>book/src/tooling/cli-usage.md</c>,
/// <c>specs/cli-build-run-commands/requirements.md</c> (Requirement 7),
/// <c>specs/attributes-and-test-command/requirements.md</c> (the test command's criterion 7),
/// <c>specs/csharp-interop-v2/requirements.md</c> (Requirement 10), <c>CliHelp</c> and the frozen
/// <c>CommandDiagnostics</c> texts in <c>TextFreezeTable</c>. Every refusal descriptor here says
/// "The command cannot proceed with this input", so a refused parse yields no arguments. Tests
/// that pass no project folder never touch the file system and are Tier 1; the one test that
/// names a real folder is Tier 2.
/// </summary>
public sealed class CliCommandSelectionTests
{
    // Requirement 7 of cli-build-run-commands and criterion 7 of the test command; the book says
    // "--mode lex is not compatible with the run or test commands".
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("run")]
    [InlineData("test")]
    public void RunAndTest_RefuseLexMode(string command)
    {
        var result = CliArgumentParser.Parse([command, "--mode", "lex"]);

        Assert.Null(result.Arguments);
        var error = Assert.Single(result.Errors);
        Assert.Same(CommandDiagnostics.LexCommandIncompatible, error.Descriptor);
        Assert.Equal(command, Assert.IsType<EnumValue>(Assert.Single(error.Arguments)).Token);
        Assert.Equal($"The '{command}' command is not compatible with '--mode lex'.", error.Message);
    }

    // The book: "--mode ... Selects the compiler mode for build"; its lex example is
    // "mist build my-project --mode lex".
    [Fact]
    [Trait("Tier", "1")]
    public void Build_AcceptsLexMode()
    {
        var result = CliArgumentParser.Parse(["build", "--mode", "lex"]);

        Assert.Empty(result.Errors);
        Assert.Equal(CliCommand.Build, result.Arguments!.Command);
        Assert.Equal(CompilerMode.Lex, result.Arguments.Mode);
    }

    // The book: "For inspect, the type name comes first and the project folder, if any,
    // second", and "If no project folder is specified, the current working directory is used."
    [Fact]
    [Trait("Tier", "1")]
    public void Inspect_TakesItsFirstPositionalAsTheTypeName()
    {
        var result = CliArgumentParser.Parse(["inspect", "System.String"]);

        Assert.Empty(result.Errors);
        Assert.Equal(CliCommand.Inspect, result.Arguments!.Command);
        Assert.Equal("System.String", result.Arguments.InspectTypeName);
        Assert.Equal(Directory.GetCurrentDirectory(), result.Arguments.ProjectFolder);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void Inspect_TakesItsSecondPositionalAsTheProjectFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var result = CliArgumentParser.Parse(["inspect", "System.String", tempDir]);

            Assert.Empty(result.Errors);
            Assert.Equal(CliCommand.Inspect, result.Arguments!.Command);
            Assert.Equal("System.String", result.Arguments.InspectTypeName);
            Assert.Equal(tempDir, result.Arguments.ProjectFolder);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // csharp-interop-v2 Requirement 10.9 gives the inspect command, not the parser, the refusal
    // of an omitted type name (InspectTypeRequired), so the parse succeeds without one.
    [Fact]
    [Trait("Tier", "1")]
    public void Inspect_WithoutATypeName_LeavesTheRefusalToTheCommand()
    {
        var result = CliArgumentParser.Parse(["inspect"]);

        Assert.Empty(result.Errors);
        Assert.Equal(CliCommand.Inspect, result.Arguments!.Command);
        Assert.Null(result.Arguments.InspectTypeName);
        Assert.Equal(Directory.GetCurrentDirectory(), result.Arguments.ProjectFolder);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void Version_IsSelected()
    {
        var result = CliArgumentParser.Parse(["version"]);

        Assert.Empty(result.Errors);
        Assert.Equal(CliCommand.Version, result.Arguments!.Command);
    }

    // version and the no-command help return before project-folder validation; an earlier flag
    // error still refuses the parse and is kept.
    [Theory]
    [Trait("Tier", "1")]
    [InlineData(new[] { "version", "--foobar" }, "Unknown flag: --foobar")]
    [InlineData(new[] { "version", "--verbose", "--quiet" }, "The '--verbose' and '--quiet' flags are mutually exclusive.")]
    [InlineData(new[] { "-v", "-q" }, "The '--verbose' and '--quiet' flags are mutually exclusive.")]
    public void EarlyReturningCommands_KeepFlagErrors(string[] args, string expected)
    {
        var result = CliArgumentParser.Parse(args);

        Assert.Null(result.Arguments);
        Assert.Equal(expected, Assert.Single(result.Errors).Message);
    }
}