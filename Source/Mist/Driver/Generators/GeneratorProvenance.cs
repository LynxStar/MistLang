using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mist.Driver.Generators;

public static class GeneratorProvenance
{
    public const string FileName = "provenance.json";

    public static string Serialize(ProvenanceRecord record)
    {
        using var stream = new MemoryStream();
        using(var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("Version", record.Version);
            writer.WriteString("ToolchainIdentity", record.ToolchainIdentity);
            writer.WriteString("GeneratorIdentity", record.GeneratorIdentity);
            writer.WriteString("ConsumerName", record.ConsumerName);
            writer.WriteString("ConsumerManifestPath", record.ConsumerManifestPath);
            WriteHashes(writer, "Inputs", record.Inputs);
            writer.WriteStartArray("Arguments");
            foreach(var argument in record.Arguments)
            {
                writer.WriteStringValue(argument);
            }

            writer.WriteEndArray();
            writer.WriteString("RunKey", record.RunKey);
            WriteHashes(writer, "Outputs", record.Outputs);
            writer.WriteBoolean("SeedTool", record.SeedTool);
            WriteHashes(writer, "Files", record.Files);
            writer.WriteString("ArtifactPath", record.ArtifactPath);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ProvenanceRecord? Deserialize(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var value = document.RootElement;
            var version = value.TryGetProperty("Version", out var encodedVersion) ? encodedVersion.GetInt32() : 1;
            if(version != 1)
            {
                return null;
            }

            return new ProvenanceRecord(Text(value, "ToolchainIdentity"), Text(value, "GeneratorIdentity"),
                Text(value, "ConsumerName"), Text(value, "ConsumerManifestPath"), ReadHashes(value.GetProperty("Inputs")),
                [.. value.GetProperty("Arguments").EnumerateArray().Select(argument => argument.GetString() ?? throw new JsonException())],
                Text(value, "RunKey"), ReadHashes(value.GetProperty("Outputs")), value.GetProperty("SeedTool").GetBoolean())
            {
                Files = value.TryGetProperty("Files", out var files) ? ReadHashes(files) : [],
                ArtifactPath = value.TryGetProperty("ArtifactPath", out var artifact) ? artifact.GetString() : null,
                Version = version
            };
        }
        catch(Exception exception) when(exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    private static string Text(JsonElement value, string name)
    {
        return value.GetProperty(name).GetString() ?? throw new JsonException();
    }

    private static ImmutableArray<ContentHash> ReadHashes(JsonElement value)
    {
        return [.. value.EnumerateArray().Select(item => new ContentHash(Text(item, "Path"), Text(item, "Sha256")))];
    }

    private static void WriteHashes(Utf8JsonWriter writer, string name, ImmutableArray<ContentHash> hashes)
    {
        writer.WriteStartArray(name);
        foreach(var hash in hashes)
        {
            writer.WriteStartObject();
            writer.WriteString("Path", hash.Path);
            writer.WriteString("Sha256", hash.Sha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
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