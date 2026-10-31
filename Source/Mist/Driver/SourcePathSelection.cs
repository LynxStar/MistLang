using System.Collections.Immutable;

namespace Mist.Driver;

public static class SourcePathSelection
{
    public const string GeneratedMarker = ".mist-generated";

    private const string DefaultOutputFolder = "out";

    private const string DefaultBinFolder = "bin";

    public static IEnumerable<string> EnumerateCandidates(string folder, IEnumerable<string>? claimedExtensions = null)
    {
        return EnumerateCandidates(folder, claimedExtensions ?? [],
            (root, pattern) => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories));
    }

    internal static IEnumerable<string> EnumerateCandidates(
        string folder, IEnumerable<string> claimedExtensions, Func<string, string, IEnumerable<string>> enumerateFiles)
    {
        var extensions = new[] { ".mist" }.Concat(claimedExtensions).Distinct(StringComparer.OrdinalIgnoreCase);
        return extensions.SelectMany(extension => enumerateFiles(folder, "*" + extension))
            .Concat(enumerateFiles(folder, GeneratedMarker).Where(IsMarker));
    }

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
        var files = candidates
            .Select(candidate => (Candidate: candidate, FullPath: Path.GetFullPath(candidate, basePath)))
            .ToList();
        var generatedRoots = files
            .Where(file => IsMarker(file.Candidate))
            .Select(file => Path.GetDirectoryName(file.FullPath)!)
            .Append(Path.GetFullPath(outputDirectory, basePath))
            .Append(Path.GetFullPath(binDirectory, basePath))
            .ToList();
        return files
            .Where(file => !generatedRoots.Any(root => IsWithin(file.FullPath, root)))
            .Select(file => file.Candidate)
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

    private static bool IsMarker(string path)
    {
        return Path.GetFileName(path) == GeneratedMarker;
    }
}