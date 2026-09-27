namespace Mist.CLI.Layout;

public sealed record LexArtifacts(string TokensPath, string SourcePath);

/// <summary>
/// Paths are resolved against the working directory, as the file APIs that clean and write the
/// output and bin directories resolve them, so a relative <c>--output</c> names the same tree here
/// that the clean deletes; a folder called <c>out</c> is only excluded while it is that tree.
/// </summary>
public sealed class ProjectLayout
{
    private readonly string workingDirectory;
    private readonly string projectRoot;
    private readonly string outputRoot;
    private readonly string binRoot;

    public ProjectLayout(string projectFolder, string outputDirectory, string binDirectory, string workingDirectory)
    {
        ProjectFolder = projectFolder;
        OutputDirectory = outputDirectory;
        BinDirectory = binDirectory;
        this.workingDirectory = workingDirectory;
        projectRoot = Path.GetFullPath(projectFolder, workingDirectory);
        outputRoot = Path.GetFullPath(outputDirectory, workingDirectory);
        binRoot = Path.GetFullPath(binDirectory, workingDirectory);
    }

    public string ProjectFolder { get; }

    public string OutputDirectory { get; }

    public string BinDirectory { get; }

    public string? DirectoryHoldingProject()
    {
        if(IsWithin(projectRoot, outputRoot))
        {
            return OutputDirectory;
        }

        if(IsWithin(projectRoot, binRoot))
        {
            return BinDirectory;
        }

        return null;
    }

    public IReadOnlyList<string> DiscoverSources()
    {
        return SelectSources(Directory.EnumerateFiles(ProjectFolder, "*.mist", SearchOption.AllDirectories));
    }

    public IReadOnlyList<string> SelectSources(IEnumerable<string> candidates)
    {
        return candidates
            .Where(candidate => !IsGenerated(Path.GetFullPath(candidate, workingDirectory)))
            .OrderBy(candidate => candidate, StringComparer.Ordinal)
            .ToList();
    }

    public LexArtifacts LexArtifactsFor(string sourceFile)
    {
        var relativePath = Path.GetRelativePath(projectRoot, Path.GetFullPath(sourceFile, workingDirectory));
        var sourcePath = Path.Combine(OutputDirectory, "lexer", relativePath);
        return new LexArtifacts(Path.ChangeExtension(sourcePath, ".tokens"), sourcePath);
    }

    private bool IsGenerated(string fullPath)
    {
        return IsWithin(fullPath, outputRoot) || IsWithin(fullPath, binRoot);
    }

    private static bool IsWithin(string fullPath, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, fullPath);
        return !(Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}