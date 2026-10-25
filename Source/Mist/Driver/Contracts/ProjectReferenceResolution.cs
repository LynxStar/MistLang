using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// The reference graph: Mist projects in dependency-first order, and the C# projects and assemblies they
/// declare, each manifest's in declaration order and a referenced manifest's before its referrer's.
/// </summary>
public sealed record ProjectReferenceResolution(
    ImmutableArray<ResolvedProjectReference> Projects,
    ImmutableArray<ExternalReference> ExternalReferences);