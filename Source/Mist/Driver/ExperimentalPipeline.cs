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
/// The experimental (v2) compilation pipeline:
/// Stage 4: Build ProgramModel from ASTs
/// Stage 5: Run new analyzers against the ProgramModel
/// Stage 6: Emit via new emitter consuming the ProgramModel
/// </summary>
public sealed class ExperimentalPipeline : ICompilationPipeline
{
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

        // Create the ambient type provider up front (SDK + NuGet assembly resolution).
        using var ambientProvider = AmbientTypeProviderV2Factory.Create(
            input.Options.TargetFramework,
            input.ResolvedAssemblyPaths,
            diagnostics);

        // Stage 4: Build Program Model
        var programModel = ProgramModelBuilder.Build(
            input.Compilation,
            input.Asts,
            input.Options,
            diagnostics,
            ambientProvider);

        // A null model is the one failure that cannot wait for the gate below, because there
        // is no model for the analyzers to run against. Stage 4 errors with a model in hand
        // deliberately fall through.
        if(programModel is null)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Stage 5: Run Analyzers.
        //
        // Runs even when Stage 4 reported errors, which is the point. Stopping at the first
        // failing stage means a user fixes every binding error, recompiles, and is handed a
        // fresh pile of analyzer errors that were knowable the whole time. Both stages read
        // the same model, so both can have their say before anyone is asked to act.
        //
        // The cost is that analyzers see a model containing ErrorType and unresolved
        // sentinels, so they must tolerate one rather than assume every type resolved.
        RunAnalyzers(programModel, diagnostics);

        // The combined gate for Stages 4 and 5, and for the lexing and parsing before them --
        // the driver does not stop for those either, and the same reasoning applies. Emission
        // reads the model, so a model that failed to bind, failed analysis, or was built from
        // source that did not parse cannot produce meaningful output.
        if(diagnostics.HasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Stage 6: Emit
        return Emit(programModel, input, diagnostics);
    }

    /// <summary>
    /// Stage 5 – Runs the new analyzer suite against the <see cref="IProgramModel"/>, reporting
    /// into <paramref name="diagnostics"/>.
    /// <para>
    /// Returns nothing on purpose. Whether analysis found errors is not this method's verdict to
    /// give, because the caller gates on Stage 4 and Stage 5 together and reads that off the bag.
    /// </para>
    /// </summary>
    private static void RunAnalyzers(IProgramModel programModel, DiagnosticBag diagnostics)
    {
        ProgramAnalysisRunner.Run((ProgramModelClass)programModel, diagnostics);
    }

    /// <summary>
    /// Stage 6 – Emits output via the new emitter consuming the <see cref="IProgramModel"/>.
    /// </summary>
    private static CompilerResult Emit(IProgramModel programModel, PipelineInput input, DiagnosticBag diagnostics)
    {
        var emitter = new CSharpEmitterV2(
            programModel, input.Options, input.ResolvedAssemblyPaths, diagnostics);
        return emitter.Emit();
    }
}