using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "1")]
public sealed class CliGeneratorCleanTests
{
    [Fact]
    public void GeneratorCacheCleanNeedsNoProject()
    {
        var parsed = CliArgumentParser.Parse(["clean", "--generators", "--quiet"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(CliCommand.Clean, parsed.Arguments!.Command);
        Assert.True(parsed.Arguments.Generators);
        Assert.True(parsed.Arguments.Quiet);
    }

    [Fact]
    public void GeneratorFlagBelongsToClean()
    {
        var parsed = CliArgumentParser.Parse(["version", "--generators"]);
        Assert.Null(parsed.Arguments);
        Assert.Same(Common.Diagnostics.CommandDiagnostics.UnknownFlag, Assert.Single(parsed.Errors).Descriptor);
    }
}