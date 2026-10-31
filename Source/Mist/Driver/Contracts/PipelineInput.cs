using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

/// <summary>
/// Bundles everything that the stages before Stage 4 produce and the pipeline needs to
/// execute Stages 4-6 (program model, analysis, emit).
/// </summary>
public sealed record PipelineInput(
    MistCompilation Compilation,
    ImmutableArray<string> ResolvedAssemblyPaths,
    CompilerOptions Options,
    DiagnosticBag Diagnostics,
    DiagnosticBag ResolutionDiagnostics,
    ImmutableArray<ExternalAssembly> ExternalAssemblies = default,
    ImmutableArray<ComposedProject> ComposedProjects = default,
    ImmutableArray<string> RuntimeIdentifiers = default,
    ImmutableArray<RuntimeAsset> RuntimeAssets = default,
    string? FrameworkAssetsDirectory = null,
    IAmbientTypeSource? AmbientTypes = null);