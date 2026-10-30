using System.Collections.Concurrent;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Generators;

internal static class GeneratorToolchain
{
    private static readonly ConcurrentDictionary<string, string> Identities = new(StringComparer.Ordinal);

    public static string? Identity(string targetFramework, DiagnosticBag diagnostics)
    {
        var references = SdkDiscovery.FindReferenceAssemblyPath(targetFramework, diagnostics);
        if(references is null)
        {
            return null;
        }

        return Identities.GetOrAdd(references, path => GeneratorKeys.Toolchain(
            Hashes(AppContext.BaseDirectory, "*.dll"),
            Hashes(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.mist"),
            Directory.GetFiles(path, "*.dll").Order(StringComparer.Ordinal)
                .Select(file => new ContentHash(file, GeneratorProvenance.Hash(File.ReadAllBytes(file)))),
            Environment.Version.ToString()));
    }

    private static IEnumerable<ContentHash> Hashes(string folder, string pattern)
    {
        return Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => new ContentHash(Path.GetRelativePath(folder, file), GeneratorProvenance.Hash(File.ReadAllBytes(file))));
    }
}