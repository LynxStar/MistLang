using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class SourcePathSelectionTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    private static string At(params string[] segments)
    {
        return Path.Combine([Root, .. segments]);
    }

    [Fact]
    public void ForReferencedProject_DropsItsDefaultOutputAndBinTrees()
    {
        var sources = SourcePathSelection.ForReferencedProject(At("Lib"),
        [
            At("Lib", "Program.mist"),
            At("Lib", "out", "lexer", "Program.mist"),
            At("Lib", "out", "lexer", "Alpha", "Foo.mist"),
            At("Lib", "bin", "Stale.mist"),
            At("Lib", "bin", "Debug", "Deep.mist"),
            At("Lib", "Alpha", "Foo.mist"),
        ]);

        Assert.Equal<string>([At("Lib", "Alpha", "Foo.mist"), At("Lib", "Program.mist")], sources);
    }

    [Fact]
    public void ForReferencedProject_KeepsNestedFoldersThatAreOnlyNamedOutOrBin()
    {
        string[] candidates =
        [
            At("Lib", "Alpha", "bin", "A.mist"),
            At("Lib", "src", "out", "B.mist"),
            At("Lib", "tools", "out", "lexer", "C.mist"),
        ];

        var sources = SourcePathSelection.ForReferencedProject(At("Lib"), candidates);

        Assert.Equal(candidates, sources);
    }

    [Fact]
    public void ForReferencedProject_KeepsSiblingsThatOnlyShareAPrefix()
    {
        string[] candidates =
        [
            At("Lib", "bin.mist"),
            At("Lib", "bin2", "B.mist"),
            At("Lib", "binary", "C.mist"),
            At("Lib", "out-other", "D.mist"),
            At("Lib", "out.mist"),
            At("Lib", "outlet", "E.mist"),
            At("Lib", "output", "F.mist"),
        ];

        var sources = SourcePathSelection.ForReferencedProject(At("Lib"), candidates);

        Assert.Equal(candidates, sources);
    }

    [Fact]
    public void ForReferencedProject_DropsEverythingUnderADirectoryHoldingTheMarker()
    {
        var sources = SourcePathSelection.ForReferencedProject(At("Lib"),
        [
            At("Lib", "gen", "lexer", SourcePathSelection.GeneratedMarker),
            At("Lib", "gen", "lexer", "Program.mist"),
            At("Lib", "gen", "lexer", "Alpha", "Foo.mist"),
            At("Lib", "gen", "sub", "..", "lexer", "Stale.mist"),
            At("Lib", "gen", "Real.mist"),
            At("Lib", "gen", "lexer-other", "Kept.mist"),
            At("Lib", "Program.mist"),
        ]);

        Assert.Equal<string>([At("Lib", "Program.mist"), At("Lib", "gen", "Real.mist"), At("Lib", "gen", "lexer-other", "Kept.mist")], sources);
    }

    [Theory]
    [InlineData("out")]
    [InlineData("bin")]
    public void ForReferencedProject_JudgesAgainstItsOwnRootNotAnEnclosingFolder(string enclosing)
    {
        var project = At("Consumer", enclosing, "Lib");

        var sources = SourcePathSelection.ForReferencedProject(project,
        [
            Path.Combine(project, "out", "lexer", "A.mist"),
            Path.Combine(project, "bin", "B.mist"),
            Path.Combine(project, "A.mist"),
        ]);

        Assert.Equal<string>([Path.Combine(project, "A.mist")], sources);
    }

    [Theory]
    [InlineData("Lib")]
    [InlineData("Lib/")]
    [InlineData("Other/../Lib")]
    [InlineData("./Lib")]
    [InlineData("Lib/out/..")]
    public void ForReferencedProject_SelectsTheSameFilesHoweverTheProjectIsSpelled(string projectFolder)
    {
        var candidates = new[]
        {
            At("Lib", "Program.mist"),
            At("Lib", "sub", "..", "out", "lexer", "Program.mist"),
            At("Lib", ".", "bin", "Stale.mist"),
            At("Lib", "out", "..", "Kept.mist"),
            At("Lib", "out-other", "..", "out", "Gone.mist"),
        };

        var sources = SourcePathSelection.ForReferencedProject(Path.Combine(Root, projectFolder), candidates);

        Assert.Equal<string>([At("Lib", "Program.mist"), At("Lib", "out", "..", "Kept.mist")], sources);
    }

    [Fact]
    public void ForReferencedProject_ReturnsCandidatesVerbatimInOrdinalOrder()
    {
        var sources = SourcePathSelection.ForReferencedProject(At("Lib"),
        [
            At("Lib", "beta.mist"),
            At("Lib", "alpha", "Zed.mist"),
            At("Lib", "Zulu.mist"),
            At("Lib", "out", "lexer", "Alpha.mist"),
            At("Lib", "Alpha.mist"),
        ]);

        Assert.Equal<string>(
            [At("Lib", "Alpha.mist"), At("Lib", "Zulu.mist"), At("Lib", "alpha", "Zed.mist"), At("Lib", "beta.mist")],
            sources);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClaimedInputs_UseTheSameSelectionAtTheRootAndInReferences(bool referenced)
    {
        var folder = At("Lib");
        var candidates = SourcePathSelection.EnumerateCandidates(folder, [".probe"], (_, pattern) => pattern switch
        {
            "*.mist" => [At("Lib", "Program.mist")],
            "*.probe" => [At("Lib", "Part.probe"), At("Lib", "nested", "Other.probe"),
                At("Lib", "out", "Stale.probe"), At("Lib", "bin", "Stale.probe"), At("Lib", "generated", "Stale.probe")],
            SourcePathSelection.GeneratedMarker => [At("Lib", "generated", SourcePathSelection.GeneratedMarker)],
            _ => throw new InvalidOperationException(pattern),
        });

        var sources = referenced
            ? SourcePathSelection.ForReferencedProject(folder, candidates)
            : SourcePathSelection.Select(candidates, folder, At("Lib", "out"), At("Lib", "bin"));

        Assert.Equal<string>([At("Lib", "Part.probe"), At("Lib", "Program.mist"), At("Lib", "nested", "Other.probe")], sources);
    }

    [Fact]
    public void NoClaims_EnumeratesOnlyMistAndGeneratedMarkers()
    {
        var patterns = new List<string>();
        var candidates = SourcePathSelection.EnumerateCandidates(Root, [], (_, pattern) =>
        {
            patterns.Add(pattern);
            return pattern == "*.mist" ? [At("Program.mist")] : [];
        }).ToArray();

        Assert.Equal(["*.mist", SourcePathSelection.GeneratedMarker], patterns.Order(StringComparer.Ordinal));
        Assert.Equal([At("Program.mist")], candidates);
    }

    [Fact]
    public void DuplicateClaims_EnumerateEachExtensionOnce()
    {
        var patterns = new List<string>();
        _ = SourcePathSelection.EnumerateCandidates(Root, [".probe", ".PROBE", ".mist"], (_, pattern) =>
        {
            patterns.Add(pattern);
            return [];
        }).ToArray();

        Assert.Equal(["*.mist", "*.probe", SourcePathSelection.GeneratedMarker], patterns.Order(StringComparer.Ordinal));
    }
}