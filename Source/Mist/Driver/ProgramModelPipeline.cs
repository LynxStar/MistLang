using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// The v2 compilation pipeline stub: ProgramModelBuilder → new analyzers → new emitter.
/// Currently unimplemented — will replace ClassicPipeline when ready.
/// </summary>
public sealed class ProgramModelPipeline : ICompilationPipeline
{
    public CompilerResult Execute(PipelineInput input)
    {
        // TODO: ProgramModelBuilder → new analyzers → new emitter
        throw new NotImplementedException("The v2 pipeline (ProgramModel) is not yet implemented.");
    }
}