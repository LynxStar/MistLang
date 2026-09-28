using Mist.CLI.Layout;

namespace Mist.CLI.Tests;

/// <summary>
/// <see cref="ProjectLayout.DiscoverSources"/> over a real directory tree, which is what the path
/// decisions in <see cref="Layout.ProjectLayoutTests"/> cannot show: that the walk is recursive, keeps
/// to <c>.mist</c> files, and applies the exclusion to the paths the file system actually returns.
/// </summary>
[Trait("Tier", "2")]
public sealed class MistFileDiscoveryTests : IDisposable
{
    private readonly string project = Path.Combine(Path.GetTempPath(), $"mist_discovery_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if(Directory.Exists(project))
        {
            Directory.Delete(project, recursive: true);
        }
    }

    private string Create(params string[] segments)
    {
        var path = Path.Combine([project, .. segments]);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// fixture");
        return path;
    }

    [Fact]
    public void DiscoverSources_ReturnsTheProjectsOwnMistFilesAndNothingTheBuildGenerated()
    {
        var program = Create("Program.mist");
        var beta = Create("Beta", "Foo.mist");
        var alpha = Create("Alpha", "Foo.mist");
        var nested = Create("Alpha", "Deep", "Bar.mist");
        var sibling = Create("out-other", "Kept.mist");
        var beside = Create("gen", "Kept.mist");
        _ = Create("readme.txt");
        _ = Create("Alpha", "Notes.cs");
        _ = Create("out", "lexer", "Program.mist");
        _ = Create("out", "lexer", "Alpha", "Foo.mist");
        _ = Create("bin", "Stale.mist");
        _ = Create("gen", "lexer", ".mist-generated");
        _ = Create("gen", "lexer", "Alpha", "Foo.mist");

        var layout = new ProjectLayout(project, Path.Combine(project, "out"), Path.Combine(project, "bin"), Directory.GetCurrentDirectory());

        Assert.Equal([nested, alpha, beta, program, beside, sibling], layout.DiscoverSources());
    }
}