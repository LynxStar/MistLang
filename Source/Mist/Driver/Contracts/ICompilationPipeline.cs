namespace Mist.Driver.Contracts;

/// <summary>
/// Everything after parsing: program model, analysis and emit.
/// </summary>
public interface ICompilationPipeline
{
    CompilerResult Execute(PipelineInput input);
}