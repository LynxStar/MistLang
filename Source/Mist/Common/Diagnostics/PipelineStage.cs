namespace Mist.Common.Diagnostics;

public enum PipelineStage
{
    StormGeneration,
    Lexing,
    Parsing,
    SemanticModelConstruction,
    Analysis,
    Backend,
    Driver,
}