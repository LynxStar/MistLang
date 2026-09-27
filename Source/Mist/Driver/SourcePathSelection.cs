using System.Collections.Immutable;

namespace Mist.Driver;

public static class SourcePathSelection
{
    private const string DefaultOutputFolder = "out";

    private const string DefaultBinFolder = "bin";

    /// <summary>A reference records no output location, so the referencing invocation's <c>--output</c> and <c>--bin</c> play no part.</summary>
    public static ImmutableArray<string> ForReferencedProject(string projectFolder, IEnumerable<string> candidates)
    {
        var projectRoot = Path.GetFullPath(projectFolder);
        return Select(
            candidates,
            projectRoot,
            Path.Combine(projectRoot, DefaultOutputFolder),
            Path.Combine(projectRoot, DefaultBinFolder));
    }

    public static ImmutableArray<string> Select(
        IEnumerable<string> candidates,
        string basePath,
        string outputDirectory,
        string binDirectory)
    {
        var outputRoot = Path.GetFullPath(outputDirectory, basePath);
        var binRoot = Path.GetFullPath(binDirectory, basePath);
        return candidates
            .Where(candidate => !IsGenerated(Path.GetFullPath(candidate, basePath), outputRoot, binRoot))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public static bool IsWithin(string fullPath, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, fullPath);
        return !(Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static bool IsGenerated(string fullPath, string outputRoot, string binRoot)
    {
        return IsWithin(fullPath, outputRoot) || IsWithin(fullPath, binRoot);
    }
}