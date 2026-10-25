using System.Collections.Immutable;

namespace Mist.Driver;

/// <summary>
/// The canonical <c>mist.toml</c> paths of the compilations in progress in this process tree, carried to a
/// child <c>dotnet build</c> so that a bridge target's nested <c>mist build</c> can see who started it.
/// </summary>
public static class BuildChain
{
    public const string VariableName = "MIST_BUILD_CHAIN";

    public static ImmutableArray<string> Current()
    {
        return Parse(Environment.GetEnvironmentVariable(VariableName));
    }

    public static ImmutableArray<string> Parse(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? []
            : [.. value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)];
    }

    public static string Format(ImmutableArray<string> chain)
    {
        return string.Join(Path.PathSeparator, chain);
    }

    public static ImmutableArray<string> Extend(ImmutableArray<string> chain, string manifestPath)
    {
        return chain.Add(manifestPath);
    }

    public static bool Contains(ImmutableArray<string> chain, string manifestPath)
    {
        return chain.Contains(manifestPath, StringComparer.OrdinalIgnoreCase);
    }

    public static string? CycleThrough(ImmutableArray<string> chain, string manifestPath)
    {
        return Contains(chain, manifestPath) ? string.Join(" -> ", Extend(chain, manifestPath)) : null;
    }
}