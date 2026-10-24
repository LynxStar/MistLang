using System.Runtime.InteropServices;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class TargetRuntimeTests
{
    [Fact]
    public void NoPlatformAndNoRid_DeploysForTheHost()
    {
        Assert.Equal("osx-arm64", TargetRuntime.Select(null, "osx-arm64", Architecture.Arm64, null));
    }

    [Theory]
    [InlineData(TargetPlatform.MacOS, "osx-arm64", Architecture.Arm64)]
    [InlineData(TargetPlatform.Linux, "linux-musl-x64", Architecture.X64)]
    [InlineData(TargetPlatform.Windows, "win-x86", Architecture.X86)]
    public void TheHostsOwnPlatform_KeepsTheHostIdentifier(TargetPlatform platform, string hostRid, Architecture architecture)
    {
        Assert.Equal(hostRid, TargetRuntime.Select(platform, hostRid, architecture, null));
    }

    [Theory]
    [InlineData(TargetPlatform.Windows, Architecture.Arm64, "win-arm64")]
    [InlineData(TargetPlatform.Linux, Architecture.X64, "linux-x64")]
    [InlineData(TargetPlatform.Windows, Architecture.X86, "win-x86")]
    [InlineData(TargetPlatform.Linux, Architecture.Arm, "linux-arm")]
    public void AnotherPlatform_JoinsItsTokenToTheHostArchitecture(TargetPlatform platform, Architecture architecture, string expected)
    {
        Assert.Equal(expected, TargetRuntime.Select(platform, "osx-arm64", architecture, null));
    }

    [Fact]
    public void MacOSFromAnotherHost_IsOsx()
    {
        Assert.Equal("osx-x64", TargetRuntime.Select(TargetPlatform.MacOS, "linux-x64", Architecture.X64, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TargetPlatform.Windows)]
    [InlineData(TargetPlatform.MacOS)]
    public void ANamedRid_TakesPrecedence(TargetPlatform? platform)
    {
        Assert.Equal("linux-musl-arm64", TargetRuntime.Select(platform, "osx-arm64", Architecture.Arm64, "linux-musl-arm64"));
    }
}