namespace Mist.Tests.Driver;

/// <summary>
/// Runs against the test project's build output, which copies Source/Standard with the same Content
/// pattern the CLI publishes with, so it proves the layout <see cref="Mist.Driver.StdlibLoader.LoadFor"/>
/// reads without running dotnet publish.
/// </summary>
public sealed class StdlibPublishLayoutTest
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    [Fact]
    public void BuildOutput_ContainsStdlibAtAppContextBaseDirectory_WithPreservedLayout()
    {
        var runtimeStdlibRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
        Assert.True(
            Directory.Exists(runtimeStdlibRoot),
            $"Runtime stdlib root not found: {runtimeStdlibRoot}. "
                + "Ensure the MSBuild <Content Include=\"..\\..\\Source\\Standard\\**\\*.mist\" ... /> "
                + "item is declared in Mist.Tests.csproj (and Mist.CLI.csproj for publish).");

        var sourceStdlibRoot = Path.Combine(FindRepoRoot(), "Source", "Standard");
        Assert.True(
            Directory.Exists(sourceStdlibRoot),
            $"Source stdlib root not found: {sourceStdlibRoot}.");

        var expected = Directory
            .GetFiles(sourceStdlibRoot, "*.mist", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(sourceStdlibRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var actual = Directory
            .GetFiles(runtimeStdlibRoot, "*.mist", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(runtimeStdlibRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);

        Assert.Contains("Bootstrap/Int32.mist", actual);
        Assert.Contains("Bootstrap/String.mist", actual);
        Assert.Contains("Optional.mist", actual);
    }

    [Fact]
    public void BuildOutput_StdlibFilesMatchSourceContentsByteForByte()
    {
        var runtimeStdlibRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
        var sourceStdlibRoot = Path.Combine(FindRepoRoot(), "Source", "Standard");

        var sourceFiles = Directory.GetFiles(sourceStdlibRoot, "*.mist", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        foreach(var sourcePath in sourceFiles)
        {
            var relative = Path.GetRelativePath(sourceStdlibRoot, sourcePath);
            var runtimePath = Path.Combine(runtimeStdlibRoot, relative);

            Assert.True(File.Exists(runtimePath), $"Missing runtime copy: {runtimePath}");
            Assert.Equal(
                File.ReadAllBytes(sourcePath),
                File.ReadAllBytes(runtimePath));
        }
    }

    [Fact]
    public void CliCsproj_DeclaresStdlibContentItem()
    {
        // Asserting on the XML is the cheapest way to prove the declaration without invoking
        // `dotnet publish` from a unit test.
        var cliCsproj = Path.Combine(
            FindRepoRoot(), "Source", "Mist.CLI", "Mist.CLI.csproj");
        Assert.True(File.Exists(cliCsproj), $"CLI csproj not found: {cliCsproj}");

        var contents = File.ReadAllText(cliCsproj);
        Assert.Contains("Source\\Standard\\**\\*.mist", contents);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", contents);
        Assert.Contains("Standard\\%(RecursiveDir)%(Filename)%(Extension)", contents);
    }
}