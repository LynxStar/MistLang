using System.Xml.Linq;

namespace Mist.CLI.Tests;

public class CsprojConfigurationTests
{
    private static readonly XDocument CsprojXml = LoadCsproj();

    private static XDocument LoadCsproj()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null && !File.Exists(Path.Combine(dir, "Mist.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);

        var csprojPath = Path.Combine(dir, "Source", "Mist.CLI", "Mist.CLI.csproj");
        Assert.True(File.Exists(csprojPath), $"Mist.CLI.csproj not found at {csprojPath}");

        return XDocument.Load(csprojPath);
    }

    private static string? GetPropertyValue(string propertyName)
    {
        return CsprojXml
            .Descendants(propertyName)
            .FirstOrDefault()
            ?.Value;
    }

    [Fact]
    public void AssemblyName_Is_Mist()
    {
        var value = GetPropertyValue("AssemblyName");
        Assert.Equal("mist", value);
    }

    [Fact]
    public void PublishSingleFile_Is_True()
    {
        var value = GetPropertyValue("PublishSingleFile");
        Assert.Equal("true", value);
    }

    [Fact]
    public void SelfContained_Is_True()
    {
        var value = GetPropertyValue("SelfContained");
        Assert.Equal("true", value);
    }

    [Fact]
    public void PublishTrimmed_Is_True()
    {
        var value = GetPropertyValue("PublishTrimmed");
        Assert.Equal("true", value);
    }
}