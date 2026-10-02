namespace Mist.Driver.Contracts;

/// <summary>
/// Selects which compilation pipeline executes Stages 4-6.
/// </summary>
public enum PipelineMode
{
    /// <summary>
    /// ProgramModel → program analyzers → C# emitter.
    /// </summary>
    Standard,
}