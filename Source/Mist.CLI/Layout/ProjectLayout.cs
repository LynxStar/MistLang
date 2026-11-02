using Mist.Driver;

namespace Mist.CLI.Layout;

public sealed record LexArtifacts(string TokensPath, string SourcePath);

/// <summary>Paths resolve against the working directory so a relative <c>--output</c> or <c>--bin</c> names the tree the clean deletes.</summary>
public sealed class ProjectLayout
{
    private readonly string workingDirectory;
    private readonly string projectRoot;
    private readonly string outputRoot;
    private readonly string binRoot;
    private readonly string lexerDirectory;

    public ProjectLayout(string projectFolder, string outputDirectory, string binDirectory, string workingDirectory)
    {
        ProjectFolder = projectFolder;
        OutputDirectory = outputDirectory;
        BinDirectory = binDirectory;
        this.workingDirectory = workingDirectory;
        projectRoot = Path.GetFullPath(projectFolder, workingDirectory);
        outputRoot = Path.GetFullPath(outputDirectory, workingDirectory);
        binRoot = Path.GetFullPath(binDirectory, workingDirectory);
        lexerDirectory = Path.Combine(outputDirectory, "lexer");
        LexMarkerPath = Path.Combine(lexerDirectory, SourcePathSelection.GeneratedMarker);
    }

    public string ProjectFolder { get; }

    public string OutputDirectory { get; }

    public string BinDirectory { get; }

    public string LexMarkerPath { get; }

    public string? DirectoryHoldingProject()
    {
        if(SourcePathSelection.IsWithin(projectRoot, outputRoot))
        {
            return OutputDirectory;
        }

        if(SourcePathSelection.IsWithin(projectRoot, binRoot))
        {
            return BinDirectory;
        }

        return null;
    }

    public IReadOnlyList<string> DiscoverSources()
    {
        return SelectSources(SourcePathSelection.EnumerateCandidates(ProjectFolder));
    }

    public IReadOnlyList<string> SelectSources(IEnumerable<string> candidates)
    {
        return SourcePathSelection.Select(candidates, workingDirectory, outputRoot, binRoot);
    }

    public LexArtifacts LexArtifactsFor(string sourceFile)
    {
        var relativePath = Path.GetRelativePath(projectRoot, Path.GetFullPath(sourceFile, workingDirectory));
        var sourcePath = Path.Combine(lexerDirectory, relativePath);
        return new LexArtifacts(Path.ChangeExtension(sourcePath, ".tokens"), sourcePath);
    }
}