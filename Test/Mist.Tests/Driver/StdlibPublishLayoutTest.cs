namespace Mist.Tests.Driver;

/// <summary>
/// Task 28.2 test for the Mist Standard Library spec.
///
/// Verifies the MSBuild <c>&lt;Content&gt;</c> item group added to
/// <c>Source/Mist.CLI/Mist.CLI.csproj</c> and <c>Test/Mist.Tests/Mist.Tests.csproj</c>
/// produces the layout required by <see cref="Mist.Driver.StdlibLoader.LoadFor"/>:
/// every <c>.mist</c> file in <c>Source/Standard/</c> must appear under
/// <c>AppContext.BaseDirectory/Standard/</c> with its relative directory
/// structure preserved.
///
/// Runs against the test project's own build output (the same mechanism the
/// CLI's publish output uses — a shared <c>&lt;Content&gt;</c> pattern with
/// <c>CopyToOutputDirectory="PreserveNewest"</c>), so this test proves the
/// <c>dotnet publish</c> layout indirectly while staying in a fast unit-test
/// loop (Requirement 15.5).
///
/// Validates: Requirement 10.9, 15.5.
/// </summary>
public sealed class StdlibPublishLayoutTest
{
    /// <summary>
    /// Walks up from the test assembly's output directory until a directory
    /// containing <c>Mist.slnx</c> is found.
    /// </summary>
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
        // ── Where the loader looks at runtime. ───────────────────────────
        var runtimeStdlibRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
        Assert.True(
            Directory.Exists(runtimeStdlibRoot),
            $"Runtime stdlib root not found: {runtimeStdlibRoot}. "
                + "Ensure the MSBuild <Content Include=\"..\\..\\Source\\Standard\\**\\*.mist\" ... /> "
                + "item is declared in Mist.Tests.csproj (and Mist.CLI.csproj for publish).");

        // ── Source of truth: the repo's stdlib source tree. ──────────────
        var sourceStdlibRoot = Path.Combine(FindRepoRoot(), "Source", "Standard");
        Assert.True(
            Directory.Exists(sourceStdlibRoot),
            $"Source stdlib root not found: {sourceStdlibRoot}.");

        // ── Compare relative paths. ──────────────────────────────────────
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

        // Sanity: we expect at least the 15 Bootstrap types plus Optional.mist.
        Assert.Contains("Bootstrap/Int32.mist", actual);
        Assert.Contains("Bootstrap/String.mist", actual);
        Assert.Contains("Optional.mist", actual);
    }

    [Fact]
    public void BuildOutput_StdlibFilesMatchSourceContentsByteForByte()
    {
        // Files copied by <Content CopyToOutputDirectory="PreserveNewest" />
        // should be byte-for-byte identical to the source tree (no
        // transformation; TargetPath only preserves directory structure).
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
        // Quick structural sanity: the Mist.CLI.csproj must declare the
        // Content item that ships the stdlib alongside the compiler binary
        // for `dotnet publish` (Requirement 15.5). Asserting on the XML is
        // the cheapest way to prove the declaration without invoking
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