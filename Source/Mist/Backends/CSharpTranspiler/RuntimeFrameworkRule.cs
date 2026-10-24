using System.Collections.Immutable;
using System.Text.Json;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler;

internal static class RuntimeFrameworkRule
{
    private const string ImpliedFramework = "Microsoft.NETCore.App";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static ImmutableArray<string> Frameworks(string runtimeConfigJson)
    {
        try
        {
            using var document = JsonDocument.Parse(runtimeConfigJson, Lenient);
            if(document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runtimeOptions", out var options)
                || options.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var names = new List<string>();
            if(options.TryGetProperty("framework", out var single))
            {
                Add(single, names);
            }

            if(options.TryGetProperty("frameworks", out var many) && many.ValueKind == JsonValueKind.Array)
            {
                foreach(var framework in many.EnumerateArray())
                {
                    Add(framework, names);
                }
            }

            return [.. names];
        }
        catch(JsonException)
        {
            return [];
        }
    }

    public static ImmutableArray<string> Missing(IEnumerable<string> required, ImmutableArray<FrameworkReference> declared)
    {
        var known = declared.IsDefaultOrEmpty
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : declared.Select(reference => reference.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = known.Add(ImpliedFramework);
        return [.. required.Where(name => !known.Contains(name)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static void Add(JsonElement framework, List<string> names)
    {
        if(framework.ValueKind == JsonValueKind.Object
            && framework.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
            && name.GetString() is { Length: > 0 } text)
        {
            names.Add(text);
        }
    }
}