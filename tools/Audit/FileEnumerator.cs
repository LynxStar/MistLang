namespace Audit;

public static class FileEnumerator
{
    private static readonly string[] IncludedSubtrees =
    [
        "Source/Mist/",
        "Source/Mist.CLI/",
        "Source/Storm.SourceGenerator/",
    ];

    /// <summary>
    /// Returns relative paths (from repo root) of all in-scope .cs files.
    /// Excludes: Source/Mist/Generated/, bin/, obj/, non-.cs files.
    /// Includes: Source/Mist/, Source/Mist.CLI/, Source/Storm.SourceGenerator/.
    /// </summary>
    public static IReadOnlyList<string> Enumerate(string repositoryRoot)
    {
        var sourceDir = Path.Combine(repositoryRoot, "Source");

        if(!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException(
                $"Source directory not found: {sourceDir}");
        }

        var results = new List<string>();

        foreach(var absolutePath in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, absolutePath)
                .Replace('\\', '/');

            if(IsExcluded(relativePath))
            {
                continue;
            }

            if(!IsIncluded(relativePath))
            {
                continue;
            }

            results.Add(relativePath);
        }

        return results;
    }

    private static bool IsExcluded(string relativePath)
    {
        if(relativePath.Contains("/Generated/", StringComparison.Ordinal) ||
            relativePath.StartsWith("Source/Mist/Generated/", StringComparison.Ordinal))
        {
            return true;
        }

        if(relativePath.Contains("/bin/", StringComparison.Ordinal) ||
            relativePath.Contains("/obj/", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsIncluded(string relativePath)
    {
        foreach(var subtree in IncludedSubtrees)
        {
            if(relativePath.StartsWith(subtree, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}