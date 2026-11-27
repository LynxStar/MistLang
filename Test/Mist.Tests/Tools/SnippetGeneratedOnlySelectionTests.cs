using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "1")]
public sealed class SnippetGeneratedOnlySelectionTests
{
    [Fact]
    public void ReturnsNullWhenOnlyGeneratedSupportIsEmitted()
    {
        var generated = Path.Combine("assembly", "__MistSupport.cs");

        var selected = SnippetCompiler.SelectEmittedPath("Example.mist", [generated]);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectsOrdinaryFallbackWhenPrimaryIsAbsent()
    {
        var generated = Path.Combine("assembly", "__MistSupport.cs");
        var fallback = Path.Combine("assembly", "Fallback.cs");

        var selected = SnippetCompiler.SelectEmittedPath("Example.mist", [generated, fallback]);

        Assert.Equal(fallback, selected);
    }
}