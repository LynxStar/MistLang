using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler;

internal static class FacadeTypeSelection
{
    public static ImmutableArray<ForwardedType> Select(IEnumerable<ForwardingCandidate> candidates, string manifestPath)
    {
        var declared = candidates.ToList();
        var forwarded = declared
            .Where(candidate => !candidate.TypeName.Contains('/') && candidate.IsPublic && Forwards(candidate.Origin, manifestPath))
            .Select(candidate => new ForwardedType(candidate.NamespaceName, candidate.TypeName))
            .ToHashSet();

        return [.. declared
            .Select(candidate => new ForwardedType(candidate.NamespaceName, candidate.TypeName))
            .Where(type => forwarded.Contains(type with { TypeName = type.TypeName.Split('/')[0] }))
            .Distinct()
            .OrderBy(type => type.NamespaceName, StringComparer.Ordinal)
            .ThenBy(type => type.TypeName, StringComparer.Ordinal)];
    }

    public static DeclarationOrigin OriginOf(IAssembly assembly)
    {
        return assembly.Kind == MistAssemblyKind.Standard
            ? DeclarationOrigin.StandardLibrary
            : new DeclarationOrigin(DeclarationOriginKind.ComposedProject, assembly.ProjectPath);
    }

    public static Dictionary<string, DeclarationOrigin> Origins(
        IEnumerable<string> emittedFiles, IReadOnlyDictionary<string, DeclarationOrigin> unitOrigins)
    {
        var origins = new Dictionary<string, DeclarationOrigin>();
        foreach(var path in emittedFiles)
        {
            origins[path] = unitOrigins.GetValueOrDefault(path, DeclarationOrigin.SupportFile);
        }

        return origins;
    }

    private static bool Forwards(DeclarationOrigin origin, string manifestPath)
    {
        return origin.Kind != DeclarationOriginKind.ComposedProject
            || string.Equals(origin.ProjectPath, manifestPath, StringComparison.Ordinal);
    }
}