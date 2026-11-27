using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "1")]
public sealed class SnippetPrimaryEmissionTests
{
    [Fact]
    public void SelectsPrimaryEmissionWithAnEarlierCompanionAndGeneratedSupport()
    {
        var output = new Dictionary<string, string>
        {
            [Path.Combine("assembly", "FirstCompanion.cs")] = "class FirstCompanion {}",
            [Path.Combine("assembly", "__MistSupport.cs")] = "class GeneratedSupport {}",
            [Path.Combine("assembly", "PrimarySnippet.cs")] = "class PrimarySnippet {}",
        };

        var selectedPath = SnippetCompiler.SelectEmittedPath("PrimarySnippet.mist", output.Keys);

        Assert.NotNull(selectedPath);
        Assert.Equal("class PrimarySnippet {}", output[selectedPath]);
    }
}