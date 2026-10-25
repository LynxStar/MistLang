using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler;

internal static class ClosureAssetSelection
{
    private static readonly char[] Separators = ['/', '\\'];

    public static ImmutableArray<RuntimeAsset> Select(IEnumerable<string> relativePaths, ImmutableArray<string> runtimeIdentifiers)
    {
        if(runtimeIdentifiers.IsDefaultOrEmpty)
        {
            return [];
        }

        var candidates = relativePaths
            .Order(StringComparer.Ordinal)
            .Select(Candidate)
            .OfType<RuntimeAsset>()
            .ToList();

        return [.. Selected(candidates, RuntimeAssetKind.Managed, runtimeIdentifiers),
            .. Selected(candidates, RuntimeAssetKind.Native, runtimeIdentifiers)];
    }

    private static IEnumerable<RuntimeAsset> Selected(
        List<RuntimeAsset> candidates, RuntimeAssetKind kind, ImmutableArray<string> runtimeIdentifiers)
    {
        var ofKind = candidates.Where(candidate => candidate.Kind == kind).ToList();
        var winner = runtimeIdentifiers.FirstOrDefault(rid => ofKind.Any(candidate => candidate.Rid == rid));
        return winner is null ? [] : ofKind.Where(candidate => candidate.Rid == winner);
    }

    private static RuntimeAsset? Candidate(string relativePath)
    {
        return relativePath.Split(Separators) switch
        {
            [var root, var rid, var folder, _] when Is(root, "runtimes") && Is(folder, "native")
                => new RuntimeAsset(relativePath, rid, RuntimeAssetKind.Native),
            [var root, var rid, var folder, _, _] when Is(root, "runtimes") && Is(folder, "lib")
                => new RuntimeAsset(relativePath, rid, RuntimeAssetKind.Managed),
            _ => null,
        };
    }

    private static bool Is(string segment, string name)
    {
        return string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);
    }
}