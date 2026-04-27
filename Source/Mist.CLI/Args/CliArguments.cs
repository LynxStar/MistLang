namespace Mist.CLI.Args;

/// <summary>
/// The top-level CLI command verb.
/// </summary>
public enum CliCommand
{
    /// <summary>Compile the project (default).</summary>
    Build,
    /// <summary>Compile the project and execute the resulting artifact.</summary>
    Run,
}

/// <summary>
/// Parsed command-line arguments for the Mist compiler CLI.
/// </summary>
public sealed record CliArguments(
    string ProjectFolder,
    CliCommand Command = CliCommand.Build,
    string BackendId = "csharp",
    string OutputDirectory = "",
    string BinDirectory = "",
    CompilerMode Mode = CompilerMode.Compile);

/// <summary>
/// The compiler execution mode.
/// </summary>
public enum CompilerMode
{
    /// <summary>Full compilation pipeline (lex → parse → semantic → analyze → backend).</summary>
    Compile,
    /// <summary>Lex only — dump token streams to output directory.</summary>
    Lex,
}
