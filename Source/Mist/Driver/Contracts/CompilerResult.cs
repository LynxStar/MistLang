using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

public sealed record CompilerResult(
    bool Succeeded,
    string? PrimaryOutputArtifactPath,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<NativeDiagnostic> Native = default);