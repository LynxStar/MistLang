using System.Xml.Linq;

namespace Mist.CLI.Tests;

/// <summary>
/// Validates that the Mist.CLI.csproj has the required MSBuild properties
/// for single-file, self-contained publishing.
/// </summary>
public class CsprojConfigurationTests
{
    private static readonly XDocument CsprojXml = LoadCsproj();

    private static XDocument LoadCsproj()
    {
        // Walk up from the test output directory to the repo root, then into the CLI project.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Mist.slnx")))
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

    /// <summary>
    /// Validates: Requirements 1.1
    /// The CLI project SHALL define AssemblyName as "mist".
    /// </summary>
    [Fact]
    public void AssemblyName_Is_Mist()
    {
        var value = GetPropertyValue("AssemblyName");
        Assert.Equal("mist", value);
    }

    /// <summary>
    /// Validates: Requirements 1.2
    /// The CLI project SHALL define PublishSingleFile as "true".
    /// </summary>
    [Fact]
    public void PublishSingleFile_Is_True()
    {
        var value = GetPropertyValue("PublishSingleFile");
        Assert.Equal("true", value);
    }

    /// <summary>
    /// Validates: Requirements 1.3
    /// The CLI project SHALL define SelfContained as "true".
    /// </summary>
    [Fact]
    public void SelfContained_Is_True()
    {
        var value = GetPropertyValue("SelfContained");
        Assert.Equal("true", value);
    }

    /// <summary>
    /// Validates: Requirements 1.4
    /// The CLI project SHALL define PublishTrimmed as "true".
    /// </summary>
    [Fact]
    public void PublishTrimmed_Is_True()
    {
        var value = GetPropertyValue("PublishTrimmed");
        Assert.Equal("true", value);
    }
}
