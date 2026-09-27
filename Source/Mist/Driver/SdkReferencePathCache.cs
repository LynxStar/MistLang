using Mist.Common.Diagnostics;

namespace Mist.Driver;

internal sealed class SdkReferencePathCache(
    Func<string, DiagnosticBag, string?> discover,
    Func<string, bool> directoryExists,
    Func<string?> readPersisted,
    Action<string> writePersisted)
{
    private const char Separator = '\t';

    private readonly Dictionary<string, string> remembered = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public string? Find(string targetFramework, DiagnosticBag diagnostics)
    {
        lock(gate)
        {
            if(remembered.TryGetValue(targetFramework, out var path))
            {
                if(directoryExists(path))
                {
                    return path;
                }

                _ = remembered.Remove(targetFramework);
            }
        }

        if(ReadPersistedEntries().TryGetValue(targetFramework, out var persisted) && directoryExists(persisted))
        {
            lock(gate)
            {
                _ = remembered.TryAdd(targetFramework, persisted);
            }

            return persisted;
        }

        return Discover(targetFramework, diagnostics);
    }

    public string? Regenerate(string targetFramework, DiagnosticBag diagnostics)
    {
        lock(gate)
        {
            _ = remembered.Remove(targetFramework);
        }

        return Discover(targetFramework, diagnostics);
    }

    private string? Discover(string targetFramework, DiagnosticBag diagnostics)
    {
        var discovered = discover(targetFramework, diagnostics);
        if(discovered is not null)
        {
            Publish(targetFramework, discovered);
        }

        return discovered;
    }

    private void Publish(string targetFramework, string path)
    {
        lock(gate)
        {
            remembered[targetFramework] = path;

            var entries = ReadPersistedEntries();
            entries[targetFramework] = path;
            try
            {
                writePersisted(Format(entries));
            }
            catch
            {
            }
        }
    }

    private Dictionary<string, string> ReadPersistedEntries()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        string text;
        try
        {
            text = readPersisted() ?? "";
        }
        catch
        {
            return entries;
        }

        // A line naming no framework, such as a bare path, is a miss rather than inferred from the path's layout:
        // a miss costs one rediscovery, which rewrites it qualified; a wrong inference serves another framework.
        foreach(var line in text.Split('\n'))
        {
            var entry = line.TrimEnd('\r');
            var separator = entry.IndexOf(Separator);
            if(separator > 0 && separator < entry.Length - 1)
            {
                entries[entry[..separator]] = entry[(separator + 1)..];
            }
        }

        return entries;
    }

    private static string Format(Dictionary<string, string> entries)
    {
        return string.Join('\n', entries
            .Where(entry => entry.Key.AsSpan().IndexOfAny(Separator, '\r', '\n') < 0
                && entry.Value.AsSpan().IndexOfAny('\r', '\n') < 0)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}{Separator}{entry.Value}"));
    }
}