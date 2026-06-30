namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A single member mapping within a conformance declaration.
/// Maps a source member on the concrete type to a target slot on the abstract type.
/// </summary>
public interface IMemberMapping
{
    /// <summary>
    /// The name of the member on the concrete type.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// The name of the slot on the abstract type.
    /// </summary>
    string TargetName { get; }
}