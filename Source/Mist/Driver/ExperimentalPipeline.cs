using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// The experimental (v2) compilation pipeline: ProgramModel → new analyzers → new emitter.
/// Currently unimplemented — will replace ClassicPipeline when ready.
/// </summary>
public sealed class ExperimentalPipeline : ICompilationPipeline
{
    public CompilerResult Execute(PipelineInput input)
    {
        // TODO: ProgramModelBuilder → new analyzers → new emitter
        throw new NotImplementedException("The experimental pipeline (ProgramModel) is not yet implemented.");
    }
}