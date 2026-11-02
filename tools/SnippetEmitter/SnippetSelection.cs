namespace SnippetEmitter;

internal static class SnippetSelection
{
    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string[] Resolve(IEnumerable<string> paths)
    {
        var selected = new HashSet<string>(PathComparer);
        foreach(var input in paths)
        {
            var path = Path.GetFullPath(input);
            if(Directory.Exists(path))
            {
                selected.UnionWith(Directory.EnumerateFiles(path, "*.mist", SearchOption.AllDirectories)
                    .Where(file => File.Exists(Path.ChangeExtension(file, ".cs"))));
            }
            else if(File.Exists(path) && Path.GetExtension(path) == ".mist")
            {
                if(!File.Exists(Path.ChangeExtension(path, ".cs")))
                {
                    throw new ArgumentException($"Snippet has no .cs twin: {input}. Create it for a new example; select the containing chapter to regenerate users of a companion.");
                }

                _ = selected.Add(path);
            }
            else
            {
                throw new ArgumentException($"Snippet file or directory not found: {input}");
            }
        }

        return selected.Count == 0
            ? throw new ArgumentException("No .mist/.cs snippet pairs selected.")
            : [.. selected.Order(PathComparer)];
    }
}