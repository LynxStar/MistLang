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

/// <summary>
/// The standard compilation pipeline:
/// Stage 4: Build ProgramModel from ASTs
/// Stage 5: Run new analyzers against the ProgramModel
/// Stage 6: Emit via new emitter consuming the ProgramModel
/// </summary>
public sealed class StandardPipeline : ICompilationPipeline
{
    private readonly ICompilationProgressReporter progressReporter;
    private readonly Func<PipelineInput, ModelBuild> build;
    private readonly Action<IProgramModel, DiagnosticBag, AnalysisTimings> analyze;
    private readonly Func<IProgramModel, Func<string, Type?>, PipelineInput, Emission> emit;

    public StandardPipeline(ICompilationProgressReporter progressReporter)
        : this(progressReporter, Build, Analyze, Emit)
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

        // Loading, lexing and parsing PRODUCE what Stage 4 consumes, so a failure in any of
        // them means the ASTs no longer describe the user's program and nothing built from them
        // is worth reporting. This is the opposite call from Stages 4 and 5 below, which are
        // gathered together -- those two are peers reading one model, where more diagnostics
        // means more information about the same thing. Here it would only mean noise derived
        // from bad input, up to and including MIST_INTERNAL_AST_UNIT_MISMATCH, which a dropped
        // AST provokes out of Stage 4 and which says nothing about the source.
        //
        // Returning before the ambient provider is created also skips SDK and package assembly
        // resolution, which there is no point paying for on the way out.
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

        // Runs even when Stage 4 reported errors, which is the point. Stopping at the first
        // failing stage means a user fixes every binding error, recompiles, and is handed a
        // fresh pile of analyzer errors that were knowable the whole time. Both stages read
        // the same model, so both can have their say before anyone is asked to act.
        //
        // The cost is that analyzers see a model containing ErrorType and unresolved
        // sentinels, so they must tolerate one rather than assume every type resolved.
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("5", "Analysis");
        var timings = new AnalysisTimings();
        analyze(programModel, diagnostics, timings);
        var analysisElapsed = analysisStopwatch.Elapsed;
        progressReporter.ReportStageComplete("5", analysisElapsed, StandardStageDetails.Analysis(timings, analysisElapsed));

        // The combined gate for Stages 4 and 5, and for the lexing and parsing before them --
        // the driver does not stop for those either, and the same reasoning applies. Emission
        // reads the model, so a model that failed to bind, failed analysis, or was built from
        // source that did not parse cannot produce meaningful output. Stage 1.5 is judged here
        // and not at the entry, for the reason ResolutionDiagnostics gives.
        if(diagnostics.HasErrors || input.ResolutionDiagnostics.HasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        var emitStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("6", "Emit");
        var emission = emit(programModel, built.ClrType, input);
        var emitElapsed = emitStopwatch.Elapsed;
        progressReporter.ReportStageComplete(
            "6", emitElapsed, StandardStageDetails.Emission(emission.FilesWritten, emission.Result.PrimaryOutputArtifactPath));

        return emission.Result;
    }

    /// <summary>
    /// Stage 4 – The ambient type provider (SDK and NuGet assembly resolution) is created inside the
    /// stage, and lives as long as the returned build because emission resolves CLR types through it.
    /// </summary>
    private static ModelBuild Build(PipelineInput input)
    {
        var ambientProvider = AmbientTypeProviderV2Factory.Create(
            input.Options.TargetFramework,
            input.ResolvedAssemblyPaths,
            input.Diagnostics);

        try
        {
            var programModel = ProgramModelBuilder.Build(
                input.Compilation,
                input.Asts,
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

    /// <summary>
    /// Stage 5 – Runs the new analyzer suite against the <see cref="IProgramModel"/>, reporting
    /// into <paramref name="diagnostics"/>.
    /// <para>
    /// Returns nothing on purpose. Whether analysis found errors is not this method's verdict to
    /// give, because the caller gates on Stage 4 and Stage 5 together and reads that off the bag.
    /// </para>
    /// </summary>
    private static void Analyze(IProgramModel programModel, DiagnosticBag diagnostics, AnalysisTimings timings)
    {
        ProgramAnalysisRunner.Run((ProgramModelClass)programModel, diagnostics, timings);
    }

    /// <summary>
    /// Stage 6 – Emits output via the new emitter consuming the <see cref="IProgramModel"/>.
    /// </summary>
    private static Emission Emit(IProgramModel programModel, Func<string, Type?> clrType, PipelineInput input)
    {
        var emitter = new CSharpEmitterV2(
            programModel, input.Options, input.ResolvedAssemblyPaths, input.Diagnostics, clrType);
        var result = emitter.Emit();

        return new Emission(result, emitter.EmittedFileCount);
    }
}

internal sealed record ModelBuild(IProgramModel? Model, Func<string, Type?> ClrType, IDisposable? Lifetime = null) : IDisposable
{
    public void Dispose()
    {
        Lifetime?.Dispose();
    }
}

internal sealed record Emission(CompilerResult Result, int FilesWritten);