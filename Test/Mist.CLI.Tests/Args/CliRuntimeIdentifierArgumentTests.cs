using Mist.CLI.Args;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "1")]
public sealed class CliRuntimeIdentifierArgumentTests
{
    [Fact]
    public void ARidIsCarriedAsGiven()
    {
        var result = CliArgumentParser.Parse(["build", "--rid", "linux-musl-x64"]);

        Assert.Empty(result.Errors);
        Assert.Equal("linux-musl-x64", result.Arguments!.RuntimeIdentifier);
    }

    [Fact]
    public void ARidAndAPlatform_AreBothCarried()
    {
        var result = CliArgumentParser.Parse(["build", "--platform", "windows", "--rid", "linux-x64"]);

        Assert.Empty(result.Errors);
        Assert.Equal(TargetPlatform.Windows, result.Arguments!.Platform);
        Assert.Equal("linux-x64", result.Arguments.RuntimeIdentifier);
    }

    [Fact]
    public void NoRid_LeavesItToTheCompiler()
    {
        var result = CliArgumentParser.Parse(["build"]);

        Assert.Empty(result.Errors);
        Assert.Null(result.Arguments!.RuntimeIdentifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void AMissingRid_IsRefused(string? value)
    {
        var result = CliArgumentParser.Parse(value is null ? ["build", "--rid"] : ["build", "--rid", value]);

        Assert.Null(result.Arguments);
        Assert.Equal("Missing value for --rid flag.", Assert.Single(result.Errors).Message);
    }
}