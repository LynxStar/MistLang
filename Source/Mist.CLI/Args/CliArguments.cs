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
    /// <summary>Install the bundled Mist steering files into the project's .kiro/steering/ directory.</summary>
    Steering,
    /// <summary>Inspect a CLR type's projected and hidden members.</summary>
    Inspect,
    /// <summary>Resolve packages and framework references without building.</summary>
    Restore,
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
    CompilerMode Mode = CompilerMode.Compile,
    bool Quiet = false,
    bool Verbose = false,
    string? InspectTypeName = null);

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