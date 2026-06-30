using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;

namespace Mist.Driver.Contracts;

/// <summary>
/// Bundles everything that Stage 3 produces and the pipeline needs to
/// execute Stages 4-6 (semantic model, analysis, emit).
/// </summary>
public sealed record PipelineInput(
    MistCompilation Compilation,
    ImmutableArray<Storm.Runtime.SyntaxNode> Asts,
    IBackend Backend,
    ImmutableArray<string> ResolvedAssemblyPaths,
    CompilerOptions Options,
    ImmutableHashSet<string>? DirectReferencePaths,
    DiagnosticBag Diagnostics);