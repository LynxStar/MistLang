namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Indicates where a type definition originated from.
/// </summary>
public enum TypeOrigin
{
    /// <summary>
    /// The type was declared in Mist source code.
    /// </summary>
    Source,

    /// <summary>
    /// The type was projected from a loaded .NET assembly.
    /// </summary>
    Assembly
}