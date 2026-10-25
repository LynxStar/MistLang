using System.Collections.Immutable;
using System.Text.Json;
using Mist.Driver.Contracts;

namespace Mist.Driver;

internal static class PackageAssets
{
    public static JsonElement? Target(JsonElement root, string targetFramework)
    {
        if(Property(root, "targets") is not { ValueKind: JsonValueKind.Object } targets)
        {
            return null;
        }

        JsonElement? first = null;
        foreach(var target in targets.EnumerateObject())
        {
            if(target.Name.Contains(targetFramework, StringComparison.OrdinalIgnoreCase)
                || target.Name.Contains(LongFrameworkName(targetFramework), StringComparison.OrdinalIgnoreCase))
            {
                return target.Value;
            }

            first ??= target.Value;
        }

        return first;
    }

    public static string? RuntimeIdentifierGraphPath(JsonElement root, string targetFramework)
    {
        if(Property(root, "project") is not { } project
            || Property(project, "frameworks") is not { ValueKind: JsonValueKind.Object } frameworks)
        {
            return null;
        }

        var framework = Property(frameworks, targetFramework)
            ?? frameworks.EnumerateObject().Select(candidate => (JsonElement?)candidate.Value).FirstOrDefault();

        return framework is { } found ? Text(found, "runtimeIdentifierGraphPath") : null;
    }

    public static ImmutableArray<RuntimeAsset> RuntimeTargets(JsonElement library, string packageDirectory)
    {
        if(Property(library, "runtimeTargets") is not { ValueKind: JsonValueKind.Object } runtimeTargets)
        {
            return [];
        }

        var entries = ImmutableArray.CreateBuilder<RuntimeAsset>();
        foreach(var entry in runtimeTargets.EnumerateObject())
        {
            if(Text(entry.Value, "rid") is { } rid && KindOf(Text(entry.Value, "assetType")) is { } kind)
            {
                entries.Add(new RuntimeAsset(
                    Path.Combine(packageDirectory, entry.Name.Replace('/', Path.DirectorySeparatorChar)),
                    rid,
                    kind));
            }
        }

        return entries.ToImmutable();
    }

    public static ImmutableArray<RuntimeAsset> SelectRuntimeAssets(
        JsonElement root,
        string targetFramework,
        string packagesFolder,
        ImmutableArray<string> order)
    {
        if(Target(root, targetFramework) is not { ValueKind: JsonValueKind.Object } target)
        {
            return [];
        }

        var selected = ImmutableArray.CreateBuilder<RuntimeAsset>();
        foreach(var library in target.EnumerateObject())
        {
            var parts = library.Name.Split('/');
            if(parts.Length != 2 || Text(library.Value, "type") == "project")
            {
                continue;
            }

            var packageDirectory = Path.Combine(packagesFolder, parts[0].ToLowerInvariant(), parts[1].ToLowerInvariant());
            selected.AddRange(RuntimeAssetSelection.Select(RuntimeTargets(library.Value, packageDirectory), order));
        }

        return selected.ToImmutable();
    }

    public static string LongFrameworkName(string shortTfm)
    {
        return shortTfm.StartsWith("net", StringComparison.Ordinal) && shortTfm.Contains('.')
            ? $".NETCoreApp,Version=v{shortTfm[3..]}"
            : shortTfm;
    }

    private static RuntimeAssetKind? KindOf(string? assetType)
    {
        return assetType switch
        {
            "native" => RuntimeAssetKind.Native,
            "runtime" => RuntimeAssetKind.Managed,
            _ => null,
        };
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;
    }

    private static string? Text(JsonElement element, string name)
    {
        return Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }
}