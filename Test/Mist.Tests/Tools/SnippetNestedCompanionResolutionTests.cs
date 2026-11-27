using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "3")]
public sealed class SnippetNestedCompanionResolutionTests : IDisposable
{
    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory("mist-snippet-companions-");

    [Fact]
    public void NestedCompanionResolvesRelativeToTheDeclaringFile()
    {
        var nested = scratch.CreateSubdirectory("nested");
        var main = Path.Combine(scratch.FullName, "main.mist");
        var declaring = Path.Combine(nested.FullName, "declaring.mist");
        var leaf = Path.Combine(nested.FullName, "leaf.mist");
        var decoy = Path.Combine(scratch.FullName, "leaf.mist");
        File.WriteAllText(main, "using Standard;\n// with: nested/declaring.mist\nnamespace Demo;\n");
        File.WriteAllText(declaring, "using Standard;\n// with: leaf.mist\nnamespace Demo;\n");
        File.WriteAllText(leaf, "namespace Demo;\n");
        File.WriteAllText(decoy, "namespace Decoy;\n");

        string[] expected = [declaring, leaf];

        Assert.Equal(expected.Order(StringComparer.Ordinal), SnippetCompiler.Companions(main).Order(StringComparer.Ordinal));
    }

    public void Dispose()
    {
        scratch.Delete(recursive: true);
    }
}