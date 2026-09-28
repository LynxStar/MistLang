using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class TargetPlatformTests
{
    [Theory]
    [InlineData(true, false, TargetPlatform.Windows)]
    [InlineData(false, true, TargetPlatform.MacOS)]
    [InlineData(false, false, TargetPlatform.Linux)]
    public void TheCompilingMachine_NamesItsPlatform(bool windows, bool macOS, TargetPlatform expected)
    {
        Assert.Equal(expected, TargetPlatforms.Of(windows, macOS));
    }
}