using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.CLI.Tests;

public class SampleProjectTomlTests
{
    /// <summary>
    /// The test assembly typically runs from Test/Mist.CLI.Tests/bin/Debug/net10.0/.
    /// </summary>
    private static string GetRepoRoot()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "samples")),
            $"Could not locate 'samples' directory. Resolved repo root: {repoRoot}");
        return repoRoot;
    }

    [Theory]
    [InlineData("HelloMist")]
    [InlineData("AllFeaturesV2")]
    [InlineData("Simpliest")]
    [InlineData("Inheritance")]
    [InlineData("AbstractTypes")]
    [InlineData("Properties")]
    public void SampleProject_HasValidMistToml(string sampleName)
    {
        var repoRoot = GetRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", sampleName);

        Assert.True(Directory.Exists(sampleDir),
            $"Sample directory '{sampleDir}' does not exist.");

        var diagnostics = new DiagnosticBag();
        var config = ProjectFileParser.Parse(sampleDir, diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Unexpected diagnostics for sample '{sampleName}': " +
            string.Join("; ", diagnostics.ToImmutableArray().Select(d => d.Message)));
        Assert.NotNull(config);
        Assert.Equal(sampleName, config.Name);
    }
}