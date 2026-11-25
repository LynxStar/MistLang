using SnippetEmitter;

namespace Mist.Tests.Tools;

// Console.SetOut and Console.SetError replace process-wide writers, so these tests run alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SnippetConsoleCollection
{
    public const string Name = "Snippet console";
}

[Trait("Tier", "3")]
[Collection(SnippetConsoleCollection.Name)]
public sealed class SnippetApplicationTests : IDisposable
{
    private static readonly DateTime Stamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(Path.GetTempPath(), $"mist-snippet-run-{Guid.NewGuid():N}");

    [Fact]
    public async Task UsageErrorsExitOneWithOneErrorLineBeforeAnyGeneration()
    {
        var unpaired = Write("unpaired.mist", "");
        var missing = Path.Combine(root, "missing.mist");

        string[][] cases = [["--bogus"], [missing], ["--jobs", "1", unpaired]];
        foreach(var args in cases)
        {
            var (exit, stdout, stderr) = await Run(args);

            Assert.Equal(1, exit);
            Assert.Equal("", stdout);
            var line = Assert.Single(Lines(stderr));
            Assert.StartsWith("error: ", line);
            Assert.Contains(args[^1], line);
        }
    }

    [Fact]
    public async Task HelpExitsZeroWithoutResolvingPaths()
    {
        var (exit, stdout, stderr) = await Run(["--help", Path.Combine(root, "missing.mist")]);

        Assert.Equal(0, exit);
        Assert.StartsWith("Usage: ", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task ListPrintsTheSelectionAndWritesNothing()
    {
        var first = Write("chapter/first.mist", "");
        var twin = Twin("chapter/first.cs");
        var second = Write("chapter/nested/second.mist", "");
        _ = Twin("chapter/nested/second.cs");

        var (exit, stdout, stderr) = await Run(["--list", "--jobs", "1", Path.Combine(root, "chapter")]);

        Assert.Equal(0, exit);
        Assert.Equal("", stderr);
        var lines = Lines(stdout);
        Assert.Equal(3, lines.Length);
        Assert.Equal(new[] { first, second }.Order(SnippetSelection.PathComparer), lines[..2].Select(Path.GetFullPath));
        Assert.Equal("Selected 2 snippet pairs.", lines[2]);
        AssertUntouched(twin);
    }

    [Fact]
    public async Task FailedSnippetsAreCountedReportedAndLeaveTheirTwins()
    {
        var first = Write("chapter/first.mist", "// with: absent.mist\n");
        var firstTwin = Twin("chapter/first.cs");
        var second = Write("chapter/second.mist", "// with: absent.mist\n");
        var secondTwin = Twin("chapter/second.cs");

        var (exit, stdout, stderr) = await Run(["--jobs", "1", Path.Combine(root, "chapter")]);

        Assert.Equal(1, exit);
        var lines = Lines(stdout);
        Assert.Equal("Found 2 .mist/.cs snippet pairs; using 1 worker(s).", lines[0]);
        Assert.Contains("Done: 0 updated, 0 unchanged, 2 failed", lines);
        Assert.DoesNotContain(lines, line => line.Contains("updated: ", StringComparison.Ordinal));
        var failures = Lines(stderr).Where(line => line.StartsWith("  FAILED", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Contains(Path.GetFileName(first), failures[0]);
        Assert.Contains(Path.GetFileName(second), failures[1]);
        AssertUntouched(firstTwin);
        AssertUntouched(secondTwin);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> Run(string[] args)
    {
        var (output, error) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await SnippetApplication.Run(args);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }
    }

    private static string[] Lines(string text)
    {
        return text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void AssertUntouched(string twin)
    {
        Assert.Equal("previous output", File.ReadAllText(twin));
        Assert.Equal(Stamp, File.GetLastWriteTimeUtc(twin));
    }

    private string Twin(string relative)
    {
        var path = Write(relative, "previous output");
        File.SetLastWriteTimeUtc(path, Stamp);
        return path;
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine([root, .. relative.Split('/')]);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if(Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}