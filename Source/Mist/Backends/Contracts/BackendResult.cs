using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Backends.Contracts;

/// <summary>
/// The result of a backend emission.
/// </summary>
public sealed record BackendResult(
    bool Succeeded,
    string? PrimaryArtifactPath,
    ImmutableArray<Diagnostic> Diagnostics,
    int EmittedFileCount = 0,
    int RuntimeAssetsDeployed = 0);