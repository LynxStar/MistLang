using Mist.Driver;

namespace Mist.Tests.Driver;

// Pure string inputs: the roots below are only joined, never touched on disk.
[Trait("Tier", "1")]
public sealed class SdkDiscoveryParsingTests
{
    private static readonly string LowerRoot = Path.Combine(Path.GetTempPath(), "dotnet-a");
    private static readonly string HighestRoot = Path.Combine(Path.GetTempPath(), "dot net b");
    private static readonly string MiddleRoot = Path.Combine(Path.GetTempPath(), "dotnet-c");

    private static string Line(string version, string root)
    {
        return $"{version} [{Path.Combine(root, "sdk")}]";
    }

    [Fact]
    public void TheHighestNumericVersion_NamesTheRoot_WhereverItIsListed()
    {
        var output = string.Join('\n',
            Line("9.0.300", LowerRoot),
            Line("10.0.100", HighestRoot),
            Line("10.0.99", MiddleRoot));

        Assert.Equal(HighestRoot, SdkDiscovery.ParseSdkListOutput(output));
    }

    [Fact]
    public void CrlfAndIndentedLines_AreParsedLikeBareLines()
    {
        var output = $"  {Line("8.0.400", LowerRoot)}\r\n{Line("10.0.100", HighestRoot)}  \r\n";

        Assert.Equal(HighestRoot, SdkDiscovery.ParseSdkListOutput(output));
    }

    [Theory]
    [InlineData("10.0.100-preview.1")]
    [InlineData("10.0.100.5")]
    [InlineData("10.0")]
    [InlineData("2147483648.0.0")]
    public void ALineWithoutAPlainNumericVersion_IsSkipped(string version)
    {
        var output = string.Join('\n', Line(version, HighestRoot), Line("9.0.100", LowerRoot));

        Assert.Equal(LowerRoot, SdkDiscovery.ParseSdkListOutput(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n \n")]
    [InlineData("No .NET SDKs were found.")]
    [InlineData("10.0.100 /usr/share/dotnet/sdk")]
    [InlineData("10.0.100-rc.2 [/usr/share/dotnet/sdk]")]
    public void OutputWithNoUsableLine_FindsNoRoot(string output)
    {
        Assert.Null(SdkDiscovery.ParseSdkListOutput(output));
    }

    [Theory]
    [InlineData("net8.0", 8)]
    [InlineData("net10.0", 10)]
    [InlineData("NET10.0", 10)]
    [InlineData("net10.0-windows", 10)]
    public void ADottedNetTfm_YieldsItsMajor(string tfm, int expected)
    {
        Assert.Equal(expected, SdkDiscovery.ExtractTfmMajorVersion(tfm));
    }

    [Theory]
    [InlineData("netstandard2.0")]
    [InlineData("netcoreapp3.1")]
    [InlineData("net48")]
    [InlineData("net.0")]
    [InlineData("netX.0")]
    [InlineData("")]
    public void ATfmWithoutADottedNetMajor_YieldsNoMajor(string tfm)
    {
        Assert.Null(SdkDiscovery.ExtractTfmMajorVersion(tfm));
    }
}