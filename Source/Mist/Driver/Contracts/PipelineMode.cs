namespace Mist.Driver.Contracts;

/// <summary>
/// Selects which compilation pipeline executes Stages 4-6.
/// </summary>
public enum PipelineMode
{
    /// <summary>
    /// The classic pipeline: semantic model → analyzers → C# emitter.
    /// </summary>
    Classic,

    /// <summary>
    /// The experimental pipeline: ProgramModel → new analyzers → new emitter.
    /// </summary>
    Experimental,
}