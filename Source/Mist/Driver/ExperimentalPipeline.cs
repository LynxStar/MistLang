using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;

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

        // Stage 4: Build Program Model
        var programModel = BuildProgramModel(input, diagnostics);
        if(programModel is null)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Stage 5: Run Analyzers
        var hasErrors = RunAnalyzers(programModel, input, diagnostics);
        if(hasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Stage 6: Emit
        return Emit(programModel, input, diagnostics);
    }

    /// <summary>
    /// Stage 4 – Builds the <see cref="IProgramModel"/> from the parsed ASTs.
    /// Returns null on fatal construction failure.
    /// </summary>
    private static IProgramModel? BuildProgramModel(PipelineInput input, DiagnosticBag diagnostics)
    {
        return ProgramModelBuilder.Build(input.Compilation, input.Asts, input.Options, diagnostics);
    }

    /// <summary>
    /// Stage 5 – Runs the new analyzer suite against the <see cref="IProgramModel"/>.
    /// Returns true if any analyzer produced an error.
    /// </summary>
    private static bool RunAnalyzers(IProgramModel programModel, PipelineInput input, DiagnosticBag diagnostics)
    {
        // TODO: Run new analyzers that consume IProgramModel
        _ = programModel;
        _ = input;
        _ = diagnostics;
        return false;
    }

    /// <summary>
    /// Stage 6 – Emits output via the new emitter consuming the <see cref="IProgramModel"/>.
    /// </summary>
    private static CompilerResult Emit(IProgramModel programModel, PipelineInput input, DiagnosticBag diagnostics)
    {
        // TODO: Invoke new emitter that consumes IProgramModel
        _ = programModel;
        _ = input;
        _ = diagnostics;

        return new CompilerResult(false, null, diagnostics.ToImmutableArray());
    }
}