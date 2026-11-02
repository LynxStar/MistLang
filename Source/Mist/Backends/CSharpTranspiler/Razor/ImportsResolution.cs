using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal static class ImportsResolution
{
    public const string FileName = "_Imports.razor";

    public static bool IsImports(string path)
    {
        return string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase);
    }

    public static string Folder(string path)
    {
        return Normalize(Path.GetDirectoryName(Normalize(path))!);
    }

    public static ImmutableArray<T> For<T>(string componentPath, string projectFolder, IReadOnlyDictionary<string, T> importsByFolder)
    {
        var byFolder = importsByFolder.ToLookup(pair => Normalize(pair.Key), pair => pair.Value, StringComparer.Ordinal);
        var root = Normalize(projectFolder);
        var chain = new List<string>();
        for(var folder = Folder(componentPath); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            chain.Add(folder);
            if(string.Equals(folder, root, StringComparison.Ordinal))
            {
                break;
            }
        }

        if(!string.Equals(chain[^1], root, StringComparison.Ordinal))
        {
            chain = [chain[0]];
        }

        chain.Reverse();
        return [.. chain.SelectMany(folder => byFolder[folder])];
    }

    // A backslash separates folders on every platform, as MSBuild reads one, so a path compares the same wherever it is resolved.
    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('\\', '/')));
    }
}