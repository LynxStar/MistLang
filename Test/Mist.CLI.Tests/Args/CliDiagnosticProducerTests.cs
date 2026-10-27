using Mist.CLI.Args;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "1")]
public sealed class CliDiagnosticProducerTests
{
    [Theory]
    [InlineData("--backend")]
    [InlineData("--output")]
    [InlineData("--bin")]
    [InlineData("--mode")]
    [InlineData("--pipeline")]
    [InlineData("--platform")]
    [InlineData("--rid")]
    public void MissingValuesCarryTheFlagAsANamedFact(string flag)
    {
        var result = CliArgumentParser.Parse([flag]);

        var error = Assert.Single(result.Errors);
        Assert.Same(CommandDiagnostics.MissingFlagValue, error.Descriptor);
        Assert.Equal(flag, Assert.IsType<NameValue>(Assert.Single(error.Arguments)).Name);
        Assert.Equal($"Missing value for {flag} flag.", error.Message);
    }
}