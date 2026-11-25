using Mist.CLI.Args;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "1")]
public sealed class CliModeArgumentTests
{
    [Fact]
    public void NoMode_SelectsCompile()
    {
        var result = CliArgumentParser.Parse(["build"]);

        Assert.Empty(result.Errors);
        Assert.Equal(CompilerMode.Compile, result.Arguments!.Mode);
    }

    [Theory]
    [InlineData("compile", CompilerMode.Compile)]
    [InlineData("lex", CompilerMode.Lex)]
    [InlineData("COMPILE", CompilerMode.Compile)]
    [InlineData("Lex", CompilerMode.Lex)]
    public void AModeIsNamed(string value, CompilerMode expected)
    {
        var result = CliArgumentParser.Parse(["build", "--mode", value]);

        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Arguments!.Mode);
    }

    // The run row also proves an unknown mode is not read as lex, which would add the lex refusal.
    [Theory]
    [InlineData("build")]
    [InlineData("run")]
    public void AnUnknownMode_IsRefusedAsGiven(string command)
    {
        var result = CliArgumentParser.Parse([command, "--mode", "Fast"]);

        Assert.Null(result.Arguments);
        var error = Assert.Single(result.Errors);
        Assert.Same(CommandDiagnostics.UnknownMode, error.Descriptor);
        Assert.Equal("Fast", Assert.IsType<NameValue>(Assert.Single(error.Arguments)).Name);
        Assert.Equal("Unknown mode: 'Fast'. Valid modes: compile, lex", error.Message);
    }
}