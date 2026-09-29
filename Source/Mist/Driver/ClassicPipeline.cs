using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;

namespace Mist.Driver;

/// <summary>
/// The classic (v1) compilation pipeline: Stages 4-6
/// (semantic model → analyzers → emit via backend).
/// </summary>
public sealed class ClassicPipeline : ICompilationPipeline
{
    private readonly ISemanticModelBuilder semanticModelBuilder;
    private readonly ImmutableArray<IAnalyzer> analyzers;
    private readonly ImmutableDictionary<string, IBackend> backends;
    private readonly ICompilationProgressReporter progressReporter;

    public ClassicPipeline(
        ISemanticModelBuilder semanticModelBuilder,
        ImmutableArray<IAnalyzer> analyzers,
        ImmutableArray<IBackend> backends,
        ICompilationProgressReporter progressReporter)
    {
        this.semanticModelBuilder = semanticModelBuilder;
        this.analyzers = analyzers;
        this.backends = backends.ToImmutableDictionary(b => b.Id.Value);
        this.progressReporter = progressReporter;
    }

    public CompilerResult Execute(PipelineInput input)
    {
        var allDiagnostics = input.Diagnostics;
        var backend = backends[input.Options.Backend.Value];

        // Dropped after parsing rather than at load because the stdlib parse cache pairs its
        // syntax trees with the stdlib units by index and serves both pipelines.
        var (compilation, asts) = WithoutExperimentalStdlib(input.Compilation, input.Asts);

        // Stage 4: Semantic Model
        var semanticResult = BuildSemanticModel(
            compilation,
            asts,
            backend,
            input.ResolvedAssemblyPaths,
            input.Options,
            input.DirectReferencePaths,
            allDiagnostics);

        if(semanticResult.Model is null || semanticResult.HasErrors)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        var semanticModel = semanticResult.Model;

        // Stage 5: Analysis
        var hasAnalysisErrors = RunAnalysis(semanticModel, input.Options, allDiagnostics);
        if(hasAnalysisErrors)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Stage 6: Emit via backend
        var emitResult = Emit(semanticModel, backend, input.Options, allDiagnostics);

        return new CompilerResult(
            emitResult.Succeeded,
            emitResult.PrimaryArtifactPath,
            allDiagnostics.ToImmutableArray(),
            emitResult.EmittedFiles);
    }

    private static (MistCompilation Compilation, ImmutableArray<Storm.Runtime.SyntaxNode> Asts) WithoutExperimentalStdlib(
        MistCompilation compilation, ImmutableArray<Storm.Runtime.SyntaxNode> asts)
    {
        var units = compilation.Units;

        for(var index = units.Length - 1; index >= 0; index--)
        {
            if(units[index].IsExperimentalOnly)
            {
                units = units.RemoveAt(index);

                if(index < asts.Length)
                {
                    asts = asts.RemoveAt(index);
                }
            }
        }

        return (compilation with { Units = units }, asts);
    }

    /// <summary>
    /// Result of Stage 4 (Semantic Model): the built model (or null on catastrophic
    /// failure) and whether the semantic-specific diagnostic bag contained errors.
    /// </summary>
    private sealed record SemanticModelResult(ISemanticModel? Model, bool HasErrors);

    /// <summary>
    /// Stage 4 – Semantic Model: builds the semantic model from the parsed ASTs,
    /// runs inference engines (readonly, IClonable derivation, synthetic drop),
    /// and returns the model with an error flag. Adds all semantic diagnostics
    /// to <paramref name="allDiagnostics"/>.
    /// </summary>
    private SemanticModelResult BuildSemanticModel(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        IBackend backend,
        ImmutableArray<string> resolvedAssemblyPaths,
        CompilerOptions options,
        ImmutableHashSet<string>? directReferencePaths,
        DiagnosticBag allDiagnostics)
    {
        var semanticStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("4", "Semantic Model");

        var ambientTypeResolver = backend.GetAmbientTypeResolver(options.TargetFramework, resolvedAssemblyPaths);
        var semanticBag = new DiagnosticBag();
        ISemanticModel? semanticModel;
        try
        {
            semanticModel = semanticModelBuilder.Build(compilation, asts, ambientTypeResolver, options.NamespaceRoot, options.ProjectFolder, semanticBag, directReferencePaths);
        }
        catch(Exception ex)
        {
            allDiagnostics.Report(
                SemanticDiagnostics.SemanticFailure,
                default,
                "<unknown>",
                (object?)ex.Message);
            semanticModel = null;
        }

        semanticStopwatch.Stop();
        var topLevelConstructs = semanticModel?.AllSymbols.Count(s =>
            s.Origin == SymbolOrigin.UserCode &&
            s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract) ?? 0;
        progressReporter.ReportStageComplete("4", semanticStopwatch.Elapsed, new[]
        {
            $"{topLevelConstructs} top-level constructs",
        });

        foreach(var d in semanticBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(semanticModel is null)
        {
            return new SemanticModelResult(null, true);
        }

        if(semanticBag.HasErrors)
        {
            return new SemanticModelResult(semanticModel, true);
        }

        // Stage 4b: Readonly Inference
        // Run the inference engine after the semantic model is built but before
        // analyzers. This classifies members as InferredReadonly or Mutable.
        ReadonlyInferenceEngine.RunInference(semanticModel);

        // Stage 4c.5: IClonable Derivation (Ownership v2, Phase 2.5)
        // Computes ImplementsIClonable for every user and bootstrap class/struct
        // type — true if the type explicitly declares IClonable<T> or every field's
        // type implements IClonable<T> (recursive, cycle-safe greatest fixpoint).
        // Must run after the semantic model is built and before the OwnershipAnalyzer
        // (Stage 5) which consults the cached result via MoveBoundaryResolver.
        IClonableDerivationEngine.RunInference(semanticModel);

        // Stage 4d: Synthetic Drop Inference
        // Synthesizes a void Drop() for types that have droppable fields but no
        // explicit Drop(), cascading the obligation transitively in dependency
        // order. Runs after the semantic model is built because it reads
        // the finalized IsOwnedType / Discipline state.
        SyntheticDropInferenceEngine.RunInference(semanticModel);

        return new SemanticModelResult(semanticModel, false);
    }

    /// <summary>
    /// Stage 5 – Analysis: filters enabled analyzers, runs them against the
    /// semantic model, and adds any diagnostics to the shared bag.
    /// Returns <c>true</c> if any analyzer produced an error diagnostic.
    /// </summary>
    private bool RunAnalysis(ISemanticModel semanticModel, CompilerOptions options, DiagnosticBag allDiagnostics)
    {
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("5", "Analysis");

        var filteredAnalyzers = options.EnabledAnalyzerIds.IsEmpty
            ? analyzers
            : analyzers.Where(a => options.EnabledAnalyzerIds.Contains(a.AnalyzerId)).ToImmutableArray();

        var runner = new AnalyzerRunner(filteredAnalyzers);
        var analyzerResult = runner.RunAll(semanticModel);

        analysisStopwatch.Stop();
        var analyzerDetails = analyzerResult.Timings
            .Select(entry => $"{entry.AnalyzerId}  {entry.Elapsed.TotalSeconds:F2}s")
            .ToArray();
        progressReporter.ReportStageComplete("5", analysisStopwatch.Elapsed, analyzerDetails);

        foreach(var d in analyzerResult.Diagnostics)
        {
            allDiagnostics.Add(d);
        }

        return analyzerResult.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// Result of Stage 6 (Emit): whether emit succeeded, the primary artifact
    /// path (when emitting to disk), and any in-memory emitted files.
    /// </summary>
    private sealed record EmitResult(
        bool Succeeded,
        string? PrimaryArtifactPath,
        IReadOnlyDictionary<string, string>? EmittedFiles);

    /// <summary>
    /// Stage 6 – Emit: invokes the backend to emit C# code (to disk or memory),
    /// reports progress, and adds backend diagnostics to <paramref name="allDiagnostics"/>.
    /// </summary>
    private EmitResult Emit(
        ISemanticModel semanticModel,
        IBackend backend,
        CompilerOptions options,
        DiagnosticBag allDiagnostics)
    {
        var emitStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("6", "Emit");

        var backendBag = new DiagnosticBag();
        var projectName = options.ProjectName;

        IReadOnlyDictionary<string, string>? emittedFiles = null;
        BackendResult backendResult;

        if(options.EmitToDisk)
        {
            backendResult = backend.Emit(semanticModel, options.OutputDirectory, options.BinDirectory, projectName, backendBag, options.OutputType, options.Packages, options.FrameworkReferences, options.CsprojProperties);
        }
        else
        {
            // In-memory emit: use CSharpEmitter.EmitToMemory directly to avoid disk I/O.
            var files = Backends.CSharpTranspiler.CSharpEmitter.EmitToMemory(semanticModel, backendBag);
            emittedFiles = files;
            backendResult = new BackendResult(
                Succeeded: !backendBag.HasErrors,
                PrimaryArtifactPath: null,
                Diagnostics: backendBag.ToImmutableArray(),
                EmittedFileCount: files.Count);
        }

        emitStopwatch.Stop();
        var emitDetails = new List<string> { $"{backendResult.EmittedFileCount} .cs files written" };
        if(backendResult.PrimaryArtifactPath is not null)
        {
            emitDetails.Add(backendResult.PrimaryArtifactPath);
        }

        progressReporter.ReportStageComplete("6", emitStopwatch.Elapsed, emitDetails);

        foreach(var d in backendBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        foreach(var d in backendResult.Diagnostics)
        {
            allDiagnostics.Add(d);
        }

        return new EmitResult(
            backendResult.Succeeded,
            backendResult.PrimaryArtifactPath,
            emittedFiles);
    }
}