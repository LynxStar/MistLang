using System.Security.Cryptography;
using System.Text.Json;

namespace Mist.Driver.Generators;

public static class GeneratorProvenance
{
    public const string FileName = "provenance.json";

    public static string Serialize(ProvenanceRecord record)
    {
        return JsonSerializer.Serialize(record);
    }

    public static ProvenanceRecord? Deserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ProvenanceRecord>(text);
        }
        catch(JsonException)
        {
            return null;
        }
    }

    internal static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static ContentHash[] Snapshot(string folder)
    {
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => path != Path.Combine(folder, FileName))
            .Order(StringComparer.Ordinal)
            .Select(path => new ContentHash(Path.GetRelativePath(folder, path), Hash(File.ReadAllBytes(path))))
            .ToArray();
    }

    internal static bool Verify(string folder, ProvenanceRecord record)
    {
        if(record.Files.IsDefaultOrEmpty || record.Files.Any(file => !Safe(file.Path)))
        {
            return false;
        }

        var actual = Snapshot(folder);
        return actual.Length == record.Files.Length
            && actual.OrderBy(file => file.Path, StringComparer.Ordinal)
                .SequenceEqual(record.Files.OrderBy(file => file.Path, StringComparer.Ordinal));
    }

    internal static bool Safe(string relative)
    {
        var normalized = OutputListValidation.Normalize(relative);
        return !string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative) && !relative.Contains(':')
            && normalized != ".." && !normalized.StartsWith("../", StringComparison.Ordinal);
    }
}