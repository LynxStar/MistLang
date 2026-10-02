using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "1")]
public sealed class CliPipelineArgumentTests
{
    [Fact]
    public void NoPipeline_SelectsStandard()
    {
        var result = CliArgumentParser.Parse(["build"]);

        Assert.Empty(result.Errors);
        Assert.Equal("standard", result.Arguments!.Pipeline);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("Standard")]
    public void StandardIsNamed(string value)
    {
        var result = CliArgumentParser.Parse(["build", "--pipeline", value]);

        Assert.Empty(result.Errors);
        Assert.Equal("standard", result.Arguments!.Pipeline);
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("experimental")]
    public void ARetiredPipelineName_IsRefused(string value)
    {
        var result = CliArgumentParser.Parse(["build", "--pipeline", value]);

        Assert.Null(result.Arguments);
        Assert.Equal($"Unknown pipeline: '{value}'. Valid pipelines: standard", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void AMissingPipeline_IsRefused()
    {
        var result = CliArgumentParser.Parse(["build", "--pipeline"]);

        Assert.Null(result.Arguments);
        Assert.Equal("Missing value for --pipeline flag.", Assert.Single(result.Errors).Message);
    }
}