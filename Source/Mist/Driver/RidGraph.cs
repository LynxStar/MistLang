using System.Collections.Immutable;
using System.Text.Json;

namespace Mist.Driver;

/// <summary>
/// <see cref="BuiltIn"/> stands in when the restore names no SDK graph. It accepts every identifier, so one it does
/// not list still falls back, segment by segment, to <c>any</c>.
/// </summary>
public sealed class RidGraph
{
    private readonly ImmutableDictionary<string, ImmutableArray<string>> imports;
    private readonly bool acceptsAny;

    private RidGraph(ImmutableDictionary<string, ImmutableArray<string>> imports, bool acceptsAny)
    {
        this.imports = imports;
        this.acceptsAny = acceptsAny;
    }

    public static RidGraph BuiltIn { get; } = new(
        new Dictionary<string, ImmutableArray<string>>
        {
            ["win-x64"] = ["win"],
            ["win-arm64"] = ["win"],
            ["linux-x64"] = ["linux"],
            ["linux-arm64"] = ["linux"],
            ["osx-x64"] = ["osx"],
            ["osx-arm64"] = ["osx"],
            ["win"] = ["any"],
            ["linux"] = ["unix"],
            ["osx"] = ["unix"],
            ["unix"] = ["any"],
            ["any"] = [],
        }.ToImmutableDictionary(StringComparer.Ordinal),
        acceptsAny: true);

    public static RidGraph? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if(document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runtimes", out var runtimes)
                || runtimes.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var imports = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
            foreach(var runtime in runtimes.EnumerateObject())
            {
                imports[runtime.Name] = Imports(runtime.Value);
            }

            return imports.Count == 0 ? null : new RidGraph(imports.ToImmutable(), acceptsAny: false);
        }
        catch(JsonException)
        {
            return null;
        }
    }

    public static bool Contains(RidGraph graph, string rid)
    {
        return graph.acceptsAny || graph.imports.ContainsKey(rid);
    }

    public static ImmutableArray<string> FallbackOrder(RidGraph graph, string rid)
    {
        var order = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { rid };
        var pending = new Queue<string>([rid]);

        while(pending.TryDequeue(out var current))
        {
            order.Add(current);
            foreach(var parent in graph.ParentsOf(current))
            {
                if(seen.Add(parent))
                {
                    pending.Enqueue(parent);
                }
            }
        }

        return order.ToImmutable();
    }

    private ImmutableArray<string> ParentsOf(string rid)
    {
        if(imports.TryGetValue(rid, out var parents))
        {
            return parents;
        }

        if(!acceptsAny)
        {
            return [];
        }

        var lastDash = rid.LastIndexOf('-');
        return lastDash > 0 ? [rid[..lastDash]] : ["any"];
    }

    private static ImmutableArray<string> Imports(JsonElement runtime)
    {
        if(runtime.ValueKind != JsonValueKind.Object
            || !runtime.TryGetProperty("#import", out var parents)
            || parents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. parents.EnumerateArray()
            .Where(parent => parent.ValueKind == JsonValueKind.String)
            .Select(parent => parent.GetString()!)];
    }
}