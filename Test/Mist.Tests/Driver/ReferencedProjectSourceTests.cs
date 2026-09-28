using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class ReferencedProjectSourceTests : IDisposable
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    private readonly string fixture = Path.Combine(Path.GetTempPath(), $"mist_referenced_sources_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if(Directory.Exists(fixture))
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    [Fact]
    public void DirectAndTransitiveReferences_EachDropTheirOwnGeneratedTrees()
    {
        var leaf = new ProjectReference("Leaf", Path.Combine(Root, "Leaf", ProjectFileParser.FileName));
        var lib = new ProjectReference("Lib", Path.Combine(Root, "Lib", ProjectFileParser.FileName));
        var manifests = new Dictionary<string, ProjectConfiguration>(StringComparer.Ordinal)
        {
            [lib.Path] = new("Lib", "Lib", [leaf]),
            [leaf.Path] = new("Leaf", "Leaf", []),
        };
        var enumerated = new List<string>();
        var diagnostics = new DiagnosticBag();

        var result = ProjectReferenceResolver.Resolve(
            Path.Combine(Root, "App"),
            [lib],
            diagnostics,
            (path, _) => manifests[path],
            folder =>
            {
                enumerated.Add(folder);
                return
                [
                    Path.Combine(folder, "out", "lexer", "Program.mist"),
                    Path.Combine(folder, "Program.mist"),
                    Path.Combine(folder, "bin", "Stale.mist"),
                    Path.Combine(folder, "Nested", "out", "Kept.mist"),
                    Path.Combine(folder, "Alpha.mist"),
                ];
            });

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal([Path.Combine(Root, "Leaf"), Path.Combine(Root, "Lib")], enumerated);
        Assert.Equal(["Leaf", "Lib"], result.Select(project => project.Configuration.Name));
        Assert.All(result, project => Assert.Equal<string>(
            [
                Path.Combine(project.ProjectFolder, "Alpha.mist"),
                Path.Combine(project.ProjectFolder, "Nested", "out", "Kept.mist"),
                Path.Combine(project.ProjectFolder, "Program.mist"),
            ],
            project.SourcePaths));
    }

    [Fact]
    public void Discovery_SkipsTheGeneratedTreesOnDiskAndLeavesEveryFileInPlace()
    {
        var libFolder = Path.Combine(fixture, "Lib");
        var leafFolder = Path.Combine(fixture, "Leaf");
        var libProgram = Create(libFolder, "Program.mist");
        var libFoo = Create(libFolder, "Alpha", "Foo.mist");
        var libNested = Create(libFolder, "Alpha", "out", "Nested.mist");
        var libSibling = Create(libFolder, "out-other", "Kept.mist");
        _ = Create(libFolder, "out", "lexer", "Program.mist");
        _ = Create(libFolder, "out", "lexer", "Program.tokens");
        _ = Create(libFolder, "out", "lexer", "Alpha", "Foo.mist");
        _ = Create(libFolder, "bin", "Stale.mist");
        _ = Create(libFolder, "gen", "lexer", SourcePathSelection.GeneratedMarker);
        _ = Create(libFolder, "gen", "lexer", "Program.mist");
        _ = Create(libFolder, "notes.txt");
        var leafSource = Create(leafFolder, "Leaf.mist");
        _ = Create(leafFolder, "out", "lexer", "Leaf.mist");
        var leaf = new ProjectReference("Leaf", Path.Combine(leafFolder, ProjectFileParser.FileName));
        var lib = new ProjectReference("Lib", Path.Combine(libFolder, ProjectFileParser.FileName));
        var manifests = new Dictionary<string, ProjectConfiguration>(StringComparer.Ordinal)
        {
            [lib.Path] = new("Lib", "Lib", [leaf]),
            [leaf.Path] = new("Leaf", "Leaf", []),
        };
        var before = Snapshot();
        var diagnostics = new DiagnosticBag();

        var result = ProjectReferenceResolver.Resolve(
            Path.Combine(fixture, "App"),
            [lib],
            diagnostics,
            (path, _) => manifests[path],
            ProjectReferenceResolver.EnumerateSources);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Collection(result,
            project => Assert.Equal<string>([leafSource], project.SourcePaths),
            project => Assert.Equal<string>([libFoo, libNested, libProgram, libSibling], project.SourcePaths));
        Assert.Equal<string>(before, Snapshot());
    }

    private static string Create(string folder, params string[] segments)
    {
        var path = Path.Combine([folder, .. segments]);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, path);
        return path;
    }

    private ImmutableArray<string> Snapshot()
    {
        return Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{File.ReadAllText(path)}")
            .ToImmutableArray();
    }
}