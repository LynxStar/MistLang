using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

/// <summary>
/// The structured result of a compilation run.
/// </summary>
public sealed record CompilerResult(
    bool Succeeded,
    string? PrimaryOutputArtifactPath,
    ImmutableArray<Diagnostic> Diagnostics,
    IReadOnlyDictionary<string, string>? EmittedFiles = null);