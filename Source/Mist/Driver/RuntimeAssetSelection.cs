using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.Driver;

internal static class RuntimeAssetSelection
{
    public static ImmutableArray<RuntimeAsset> Select(ImmutableArray<RuntimeAsset> entries, ImmutableArray<string> order)
    {
        var chosen = new Dictionary<RuntimeAssetKind, string>();
        foreach(var kind in entries.Select(entry => entry.Kind).Distinct())
        {
            var rid = order.FirstOrDefault(candidate => entries.Any(entry => entry.Kind == kind && entry.Rid == candidate));
            if(rid is not null)
            {
                chosen[kind] = rid;
            }
        }

        return [.. entries.Where(entry => chosen.TryGetValue(entry.Kind, out var rid) && entry.Rid == rid)];
    }
}