using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <param name="Resolved">Only frameworks whose reference pack exists and holds at least one assembly.</param>
public sealed record FrameworkResolutionResult(
    ImmutableArray<string> AssemblyPaths,
    ImmutableArray<ResolvedFrameworkInfo> Resolved,
    int FailureCount);

/// <param name="Version">The selected reference pack's version.</param>
public sealed record ResolvedFrameworkInfo(
    string Name,
    string Version);