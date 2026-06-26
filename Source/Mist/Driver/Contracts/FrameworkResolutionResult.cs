using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// The structured outcome of resolving the enabled <c>[framework_references]</c>
/// entries. Carries the flattened reference-assembly paths contributed by every
/// successfully resolved framework pack, a per-resolved-framework summary, and
/// the count of enabled frameworks that failed to resolve.
/// </summary>
/// <param name="AssemblyPaths">
/// All reference-assembly file paths contributed by every successfully resolved
/// framework reference pack.
/// </param>
/// <param name="Resolved">
/// One entry per enabled framework reference that was located successfully
/// (the pack directory exists and contains at least one reference assembly).
/// </param>
/// <param name="FailureCount">
/// The number of enabled frameworks that produced an error-severity diagnostic
/// during resolution.
/// </param>
public sealed record FrameworkResolutionResult(
    ImmutableArray<string> AssemblyPaths,
    ImmutableArray<ResolvedFrameworkInfo> Resolved,
    int FailureCount);

/// <summary>
/// A per-resolved-framework summary used by the verbose package-resolution
/// stage and the <c>mist restore</c> command.
/// </summary>
/// <param name="Name">The enabled shared-framework name (e.g., <c>Microsoft.AspNetCore.App</c>).</param>
/// <param name="Version">The selected reference pack version (e.g., <c>10.0.0</c>).</param>
public sealed record ResolvedFrameworkInfo(
    string Name,
    string Version);