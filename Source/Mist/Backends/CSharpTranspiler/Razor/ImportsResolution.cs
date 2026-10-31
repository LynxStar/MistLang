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
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(path))!));
    }

    public static ImmutableArray<T> For<T>(string componentPath, string projectFolder, IReadOnlyDictionary<string, T> importsByFolder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFolder));
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
        return [.. chain.Where(importsByFolder.ContainsKey).Select(folder => importsByFolder[folder])];
    }
}