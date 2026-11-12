using System.Diagnostics;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Driver;

public sealed class StandardPipeline : ICompilationPipeline
{
    private readonly ICompilationProgressReporter progressReporter;
    private readonly Func<PipelineInput, ModelBuild> build;
    private readonly Action<IProgramModel, DiagnosticBag, AnalysisTimings> analyze;
    private readonly Func<IProgramModel, Func<string, Type?>, PipelineInput, Emission> emit;

    public StandardPipeline(
        ICompilationProgressReporter progressReporter,
        Func<PipelineInput, IAmbientTypeSource>? ambientTypes = null)
        : this(progressReporter, input => Build(input, ambientTypes ?? LoadAmbientTypes), Analyze, Emit)
    {
    }

    internal StandardPipeline(
        ICompilationProgressReporter progressReporter,
        Func<PipelineInput, ModelBuild> build,
        Action<IProgramModel, DiagnosticBag, AnalysisTimings> analyze,
        Func<IProgramModel, Func<string, Type?>, PipelineInput, Emission> emit)
    {
        this.progressReporter = progressReporter;
        this.build = build;
        this.analyze = analyze;
        this.emit = emit;
    }

    public CompilerResult Execute(PipelineInput input)
    {
        var diagnostics = input.Diagnostics;

        // Loading, lexing and parsing PRODUCE what Stage 4 consumes, so after one fails the ASTs no longer
        // describe the user's program and anything built from them is noise. Stages 4 and 5 are the opposite
        // call: peers reading one model, gathered at the gate below. Returning before the ambient provider is
        // created also skips SDK and package assembly resolution.
        if(diagnostics.HasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        var modelStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("4", "Program Model");
        using var built = build(input);
        var modelElapsed = modelStopwatch.Elapsed;
        progressReporter.ReportStageComplete("4", modelElapsed, StandardStageDetails.Model(TopLevelConstructs.Count(built.Model)));

        // A null model is the one failure that cannot wait for the gate below, because there
        // is no model for the analyzers to run against. Stage 4 errors with a model in hand
        // deliberately fall through.
        if(built.Model is not { } programModel)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Runs even when Stage 4 reported errors: both stages read the same model, so a user sees binding and
        // analyzer errors together rather than a fresh pile after fixing the first. Analyzers must therefore
        // tolerate ErrorType and unresolved sentinels.
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("5", "Analysis");
        var timings = new AnalysisTimings();
        analyze(programModel, diagnostics, timings);
        var analysisElapsed = analysisStopwatch.Elapsed;
        progressReporter.ReportStageComplete("5", analysisElapsed, StandardStageDetails.Analysis(timings, analysisElapsed));

        // The combined gate for Stages 4 and 5, since emission reads the model. Stage 1.5 is judged here and
        // not at the entry, for the reason ResolutionDiagnostics gives.
        if(diagnostics.HasErrors || input.ResolutionDiagnostics.HasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        var emitStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("6", "Emit");
        var emission = emit(programModel, built.ClrType, input);
        var emitElapsed = emitStopwatch.Elapsed;
        progressReporter.ReportStageComplete(
            "6", emitElapsed, StandardStageDetails.Emission(emission.FilesWritten, emission.Result.PrimaryOutputArtifactPath, emission.RuntimeAssetsDeployed));

        return emission.Result;
    }

    /// <summary>
    /// The ambient types are created inside Stage 4 and live as long as the returned build, because emission
    /// resolves CLR types through them.
    /// </summary>
    private static ModelBuild Build(PipelineInput input, Func<PipelineInput, IAmbientTypeSource> ambientTypes)
    {
        var ambientProvider = ambientTypes(input);

        try
        {
            var programModel = ProgramModelBuilder.Build(
                input.Compilation,
                input.Options,
                input.Diagnostics,
                ambientProvider);

            return new ModelBuild(programModel, ambientProvider.FindClrType, ambientProvider);
        }
        catch
        {
            ambientProvider.Dispose();
            throw;
        }
    }

    private static IAmbientTypeSource LoadAmbientTypes(PipelineInput input)
    {
        var externals = input.ExternalAssemblies.IsDefault ? [] : input.ExternalAssemblies;
        var composed = input.ComposedProjects.IsDefault ? [] : input.ComposedProjects;

        return AmbientTypeProviderV2Factory.Create(
            input.Options.TargetFramework,
            input.ResolvedAssemblyPaths,
            input.Diagnostics,
            [.. externals.Where(external => !external.Closure.IsDefault).SelectMany(external => external.Closure).Select(file => Path.GetDirectoryName(file)!).Distinct(StringComparer.Ordinal)],
            composed.Select(project => project.Name));
    }

    /// <summary>
    /// Returns no verdict: the caller gates on Stages 4 and 5 together and reads that off the bag.
    /// </summary>
    private static void Analyze(IProgramModel programModel, DiagnosticBag diagnostics, AnalysisTimings timings)
    {
        ProgramAnalysisRunner.Run((ProgramModelClass)programModel, diagnostics, timings);
    }

    private static Emission Emit(IProgramModel programModel, Func<string, Type?> clrType, PipelineInput input)
    {
        var emitter = new CSharpEmitterV2(
            programModel, input.Options, input.ResolvedAssemblyPaths, input.Diagnostics, clrType,
            input.ExternalAssemblies, input.ComposedProjects, input.RuntimeIdentifiers, input.RuntimeAssets);
        var result = emitter.Emit();

        return new Emission(result, emitter.EmittedFileCount, emitter.RuntimeAssetsDeployed);
    }
}

internal sealed record ModelBuild(IProgramModel? Model, Func<string, Type?> ClrType, IDisposable? Lifetime = null) : IDisposable
{
    public void Dispose()
    {
        Lifetime?.Dispose();
    }
}

internal sealed record Emission(CompilerResult Result, int FilesWritten, int RuntimeAssetsDeployed = 0);