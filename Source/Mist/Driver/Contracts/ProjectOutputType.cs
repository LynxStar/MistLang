namespace Mist.Driver.Contracts;

/// <summary>
/// Specifies the kind of output artifact a Mist project produces.
/// Controls whether the emitted .csproj targets an executable or a class library.
/// </summary>
public enum ProjectOutputType
{
    /// <summary>
    /// The project produces an executable with a Main entry point.
    /// Emits <c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c> in the .csproj.
    /// </summary>
    Executable,

    /// <summary>
    /// The project produces a class library (DLL) with no entry point requirement.
    /// Emits <c>&lt;OutputType&gt;Library&lt;/OutputType&gt;</c> in the .csproj.
    /// </summary>
    Library,
}