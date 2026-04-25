namespace Mist.Common.Diagnostics;

/// <summary>
/// Identifies the compiler pipeline stage that originated a diagnostic.
/// </summary>
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
