namespace Mist.Driver.Generators;

public static class OutputListValidation
{
    public static OutputListFault? Validate(IEnumerable<string> listed, IEnumerable<string> actual,
        IEnumerable<string> markedDirectories, StringComparer? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        var outputs = new HashSet<string>(comparer);
        var files = actual.Select(Normalize).ToHashSet(comparer);
        var marked = markedDirectories.Select(Normalize).ToArray();
        foreach(var path in listed)
        {
            if(string.IsNullOrWhiteSpace(path) || path[0] is '/' or '\\'
                || path.Contains(':') || path.AsSpan().IndexOfAny('\0', '\r', '\n') >= 0)
            {
                return new OutputListFault(path, "output must be a relative path");
            }

            var canonical = Normalize(path);
            if(canonical == ".." || canonical.StartsWith("../", StringComparison.Ordinal))
            {
                return new OutputListFault(path, "output escapes staging");
            }

            if(!outputs.Add(canonical))
            {
                return new OutputListFault(path, "duplicate output");
            }

            if(!canonical.EndsWith(".mist", StringComparison.OrdinalIgnoreCase))
            {
                return new OutputListFault(path, "output must be a .mist file");
            }

            if(canonical.Split('/').Any(part => comparer.Equals(part, "out") || comparer.Equals(part, "bin"))
                || marked.Any(directory => directory is "" or "." || comparer.Equals(canonical, directory)
                    || canonical.StartsWith(directory + "/", comparer == StringComparer.OrdinalIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            {
                return new OutputListFault(path, "output is excluded by source discovery");
            }

            if(!files.Contains(canonical))
            {
                return new OutputListFault(path, "output file is missing");
            }
        }

        if(outputs.Count == 0)
        {
            return new OutputListFault("outputs.txt", "output list is empty");
        }

        var omitted = files.FirstOrDefault(file => !outputs.Contains(file));
        return omitted is null ? null : new OutputListFault(omitted, "unlisted .mist file");
    }

    internal static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach(var part in path.Replace('\\', '/').Split('/'))
        {
            if(part is "" or ".")
            {
                continue;
            }

            if(part == ".." && parts.Count > 0 && parts[^1] != "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }
}