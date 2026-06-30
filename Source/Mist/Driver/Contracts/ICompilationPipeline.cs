namespace Mist.Driver.Contracts;

/// <summary>
/// A post-parse compilation pipeline that takes ASTs from Stage 3 and
/// produces a final CompilerResult (semantic analysis, analyzers, emit).
/// </summary>
public interface ICompilationPipeline
{
    CompilerResult Execute(PipelineInput input);
}