using Mist.CLI.Layout;

namespace Mist.CLI.Tests.Layout;

/// <summary>
/// The path decisions of <see cref="ProjectLayout"/>, over paths that exist only as strings: every
/// layout is resolved against a working directory the test names, so nothing reads the disk or the
/// process's current directory.
/// </summary>
[Trait("Tier", "1")]
public sealed class ProjectLayoutTests
{
    private static readonly string WorkingDirectory = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    private static string At(params string[] segments)
    {
        return Path.Combine([WorkingDirectory, .. segments]);
    }

    private static ProjectLayout DefaultLayout()
    {
        return new ProjectLayout(At("proj"), At("proj", "out"), At("proj", "bin"), WorkingDirectory);
    }

    [Fact]
    public void SelectSources_SkipsTheDefaultOutputAndBinTrees()
    {
        var sources = DefaultLayout().SelectSources(
        [
            At("proj", "Program.mist"),
            At("proj", "out", "lexer", "Program.mist"),
            At("proj", "out", "lexer", "Alpha", "Foo.mist"),
            At("proj", "bin", "Stale.mist"),
            At("proj", "Alpha", "Foo.mist"),
        ]);

        Assert.Equal([At("proj", "Alpha", "Foo.mist"), At("proj", "Program.mist")], sources);
    }

    [Fact]
    public void SelectSources_SkipsCustomOutputAndBinTreesAndKeepsAFolderThatIsOnlyNamedOut()
    {
        var layout = new ProjectLayout(At("proj"), At("proj", "build", "gen"), At("proj", "artifacts"), WorkingDirectory);

        var sources = layout.SelectSources(
        [
            At("proj", "build", "gen", "lexer", "Program.mist"),
            At("proj", "artifacts", "Stale.mist"),
            At("proj", "build", "Real.mist"),
            At("proj", "out", "Real.mist"),
        ]);

        Assert.Equal([At("proj", "build", "Real.mist"), At("proj", "out", "Real.mist")], sources);
    }

    [Fact]
    public void SelectSources_ResolvesARelativeProjectAndItsDefaultsAgainstTheWorkingDirectory()
    {
        var layout = new ProjectLayout("proj", Path.Combine("proj", "out"), Path.Combine("proj", "bin"), WorkingDirectory);

        var sources = layout.SelectSources(
        [
            Path.Combine("proj", "out", "lexer", "Program.mist"),
            Path.Combine("proj", "Program.mist"),
        ]);

        Assert.Equal([Path.Combine("proj", "Program.mist")], sources);
    }

    [Fact]
    public void SelectSources_ReadsARelativeOverrideFromTheWorkingDirectoryNotFromTheProject()
    {
        var layout = new ProjectLayout(At("proj"), "out", "bin", WorkingDirectory);

        var sources = layout.SelectSources([At("proj", "out", "Real.mist"), At("proj", "bin", "Real.mist")]);

        Assert.Equal([At("proj", "bin", "Real.mist"), At("proj", "out", "Real.mist")], sources);
    }

    [Theory]
    [InlineData("proj/out/")]
    [InlineData("proj/sub/../out")]
    [InlineData("proj/./out")]
    [InlineData("proj/out/lexer/..")]
    [InlineData("proj/sub/../out/")]
    public void SelectSources_ExcludesTheSameTreeHoweverTheOutputIsSpelled(string outputDirectory)
    {
        var layout = new ProjectLayout(At("proj"), outputDirectory, At("proj", "bin"), WorkingDirectory);

        var sources = layout.SelectSources([At("proj", "out", "lexer", "Program.mist"), At("proj", "Program.mist")]);

        Assert.Equal([At("proj", "Program.mist")], sources);
    }

    [Fact]
    public void SelectSources_KeepsNamesThatOnlyBeginWithOutOrBin()
    {
        string[] candidates =
        [
            At("proj", "out-other", "A.mist"),
            At("proj", "output", "B.mist"),
            At("proj", "outlet", "C.mist"),
            At("proj", "bin2", "D.mist"),
            At("proj", "binary", "E.mist"),
            At("proj", "out.mist"),
        ];

        var sources = DefaultLayout().SelectSources(candidates);

        Assert.Equal(candidates.Order(StringComparer.Ordinal), sources);
    }

    [Fact]
    public void SelectSources_ReturnsCandidatesVerbatimInOrdinalOrder()
    {
        var sources = DefaultLayout().SelectSources(
        [
            At("proj", "beta.mist"),
            At("proj", "alpha", "Zed.mist"),
            At("proj", "Zulu.mist"),
            At("proj", "Alpha.mist"),
        ]);

        Assert.Equal(
            [At("proj", "Alpha.mist"), At("proj", "Zulu.mist"), At("proj", "alpha", "Zed.mist"), At("proj", "beta.mist")],
            sources);
    }

    [Fact]
    public void SelectSources_SkipsWhatALexIntoAnotherOutputWrote()
    {
        var earlierLex = new ProjectLayout(At("proj"), At("proj", "gen"), At("proj", "bin"), WorkingDirectory);
        var foo = At("proj", "Alpha", "Foo.mist");

        var sources = DefaultLayout().SelectSources(
        [
            earlierLex.LexMarkerPath,
            earlierLex.LexArtifactsFor(foo).SourcePath,
            earlierLex.LexArtifactsFor(At("proj", "Program.mist")).SourcePath,
            foo,
        ]);

        Assert.Equal([foo], sources);
    }

    [Fact]
    public void LexMarkerPath_SitsInTheLexerDirectoryOfTheOutput()
    {
        var layout = new ProjectLayout(At("proj"), At("proj", "gen"), At("proj", "bin"), WorkingDirectory);

        Assert.Equal(At("proj", "gen", "lexer", ".mist-generated"), layout.LexMarkerPath);
    }

    [Fact]
    public void DirectoryHoldingProject_IsNullWhenOutputAndBinSitInsideTheProject()
    {
        Assert.Null(DefaultLayout().DirectoryHoldingProject());
    }

    [Theory]
    [InlineData("proj")]
    [InlineData("proj/")]
    [InlineData("proj/sub/..")]
    [InlineData(".")]
    public void DirectoryHoldingProject_NamesAnOutputThatIsOrContainsTheProject(string outputDirectory)
    {
        var layout = new ProjectLayout(At("proj"), outputDirectory, At("proj", "bin"), WorkingDirectory);

        Assert.Equal(outputDirectory, layout.DirectoryHoldingProject());
    }

    [Fact]
    public void DirectoryHoldingProject_NamesABinThatIsTheProject()
    {
        var layout = new ProjectLayout(At("proj"), At("proj", "out"), At("proj"), WorkingDirectory);

        Assert.Equal(At("proj"), layout.DirectoryHoldingProject());
    }

    [Fact]
    public void DirectoryHoldingProject_IgnoresADirectoryThatOnlySharesThePrefix()
    {
        var layout = new ProjectLayout(At("project"), At("proj"), At("project-bin"), WorkingDirectory);

        Assert.Null(layout.DirectoryHoldingProject());
    }

    [Theory]
    [InlineData("Foo.mist", "Foo.tokens")]
    [InlineData("Foo.Bar.mist", "Foo.Bar.tokens")]
    public void LexArtifactsFor_KeepsARootLevelSourcesName(string fileName, string tokensName)
    {
        var artifacts = DefaultLayout().LexArtifactsFor(At("proj", fileName));

        Assert.Equal(new LexArtifacts(At("proj", "out", "lexer", tokensName), At("proj", "out", "lexer", fileName)), artifacts);
    }

    [Fact]
    public void LexArtifactsFor_KeepsSourcesThatShareANameApartByTheirDirectory()
    {
        var layout = DefaultLayout();

        var alpha = layout.LexArtifactsFor(At("proj", "Alpha", "Foo.mist"));
        var beta = layout.LexArtifactsFor(At("proj", "Beta", "Foo.mist"));

        Assert.Equal(new LexArtifacts(At("proj", "out", "lexer", "Alpha", "Foo.tokens"), At("proj", "out", "lexer", "Alpha", "Foo.mist")), alpha);
        Assert.Equal(new LexArtifacts(At("proj", "out", "lexer", "Beta", "Foo.tokens"), At("proj", "out", "lexer", "Beta", "Foo.mist")), beta);
    }

    [Fact]
    public void LexArtifactsFor_SpellsTheOutputAsGivenForARelativeProject()
    {
        var layout = new ProjectLayout("proj", Path.Combine("proj", "out"), Path.Combine("proj", "bin"), WorkingDirectory);

        var artifacts = layout.LexArtifactsFor(Path.Combine("proj", "a", "Foo.mist"));

        Assert.Equal(
            new LexArtifacts(Path.Combine("proj", "out", "lexer", "a", "Foo.tokens"), Path.Combine("proj", "out", "lexer", "a", "Foo.mist")),
            artifacts);
    }
}