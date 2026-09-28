using Mist.CLI.Args;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "1")]
public sealed class CliPlatformArgumentTests
{
    [Theory]
    [InlineData("windows", TargetPlatform.Windows)]
    [InlineData("linux", TargetPlatform.Linux)]
    [InlineData("macos", TargetPlatform.MacOS)]
    [InlineData("Windows", TargetPlatform.Windows)]
    [InlineData("MACOS", TargetPlatform.MacOS)]
    public void APlatformIsNamed(string value, TargetPlatform expected)
    {
        var result = CliArgumentParser.Parse(["build", "--platform", value]);

        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Arguments!.Platform);
    }

    [Fact]
    public void NoPlatform_LeavesItToTheCompiler()
    {
        var result = CliArgumentParser.Parse(["build"]);

        Assert.Empty(result.Errors);
        Assert.Null(result.Arguments!.Platform);
    }

    [Fact]
    public void AnUnknownPlatform_IsRefused()
    {
        var result = CliArgumentParser.Parse(["build", "--platform", "freebsd"]);

        Assert.Null(result.Arguments);
        Assert.Equal("Unknown platform: 'freebsd'. Valid platforms: windows, linux, macos", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void AMissingPlatform_IsRefused()
    {
        var result = CliArgumentParser.Parse(["build", "--platform"]);

        Assert.Null(result.Arguments);
        Assert.Equal("Missing value for --platform flag.", Assert.Single(result.Errors).Message);
    }
}