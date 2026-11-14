using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

public sealed record PipelineInput(
    MistCompilation Compilation,
    ImmutableArray<string> ResolvedAssemblyPaths,
    CompilerOptions Options,
    DiagnosticBag Diagnostics,
    DiagnosticBag ResolutionDiagnostics,
    ImmutableArray<ExternalAssembly> ExternalAssemblies = default,
    ImmutableArray<ComposedProject> ComposedProjects = default,
    ImmutableArray<string> RuntimeIdentifiers = default,
    ImmutableArray<RuntimeAsset> RuntimeAssets = default);