using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "3")]
public sealed class SnippetFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"mist-snippet-files-{Guid.NewGuid():N}");

    [Fact]
    public void ExactFilesAndOverlappingRecursiveDirectoriesSelectEachPairOnce()
    {
        var first = Write("chapter/first.mist", "");
        _ = Write("chapter/first.cs", "");
        var second = Write("chapter/nested/second.mist", "");
        _ = Write("chapter/nested/second.cs", "");
        _ = Write("chapter/companion.mist", "");
        _ = Write("elsewhere/other.mist", "");
        _ = Write("elsewhere/other.cs", "");

        var selected = SnippetSelection.Resolve([first, Path.Combine(root, "chapter"), second]);

        Assert.Equal(new[] { first, second }.Order(SnippetSelection.PathComparer), selected);
        Assert.Equal([second], SnippetSelection.Resolve([second]));
    }

    [Fact]
    public void MissingInputsAndUnpairedExplicitFilesAreRefused()
    {
        var companion = Write("companion.mist", "");

        _ = Assert.Throws<ArgumentException>(() => SnippetSelection.Resolve([companion]));
        _ = Assert.Throws<ArgumentException>(() => SnippetSelection.Resolve([Path.Combine(root, "missing.mist")]));
        _ = Assert.Throws<ArgumentException>(() => SnippetSelection.Resolve([root]));
    }

    [Fact]
    public void CompanionTraversalIsTransitiveAndTerminatesOnCycles()
    {
        var main = Write("main.mist", "using Standard;\n// with: first.mist, second.mist\nnamespace Demo;\n");
        var first = Write("first.mist", "// with: second.mist\nnamespace Demo;\n");
        var second = Write("second.mist", "// with: main.mist\nnamespace Demo;\n");

        Assert.Equal(new[] { first, second }.Order(), SnippetCompiler.Companions(main).Order());
    }

    [Fact]
    public void FailedValidationPreservesTheExistingTwin()
    {
        var path = Write("example.mist", "");
        var twin = Write("example.cs", "previous output");

        Assert.False(SnippetOutput.Write(Result(path, null)));
        Assert.Equal("previous output", File.ReadAllText(twin));
    }

    [Fact]
    public void EqualOutputPreservesBytesAndTimestamp()
    {
        var path = Write("example.mist", "");
        var twin = Write("example.cs", "class A {}\r\n");
        File.SetLastWriteTimeUtc(twin, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var timestamp = File.GetLastWriteTimeUtc(twin);

        Assert.False(SnippetOutput.Write(Result(path, "class A {}\n")));
        Assert.Equal("class A {}\r\n", File.ReadAllText(twin));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(twin));
    }

    [Fact]
    public void ChangedOutputWritesTheActualEmissionWithLf()
    {
        var path = Write("example.mist", "");
        var twin = Write("example.cs", "old");

        Assert.True(SnippetOutput.Write(Result(path, "class A {}\r\n")));
        Assert.Equal("class A {}\n", File.ReadAllText(twin));
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static SnippetResult Result(string path, string? emitted)
    {
        return new SnippetResult(path, emitted, "", 0, 1, []);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }
}