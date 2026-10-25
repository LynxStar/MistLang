using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>The resolved reference graph with every C# project built and every external assembly found.</summary>
public sealed record LoadedReferences(
    ImmutableArray<ResolvedProjectReference> Projects,
    ImmutableArray<ExternalAssembly> ExternalAssemblies,
    int BuiltCSharpProjectCount);