using Mist.Driver.Contracts;

namespace Mist.CLI.Args;

/// <summary>
/// The top-level CLI command verb.
/// </summary>
public enum CliCommand
{
    /// <summary>No command provided — show help.</summary>
    None,
    /// <summary>Compile the project.</summary>
    Build,
    /// <summary>Compile the project and execute the resulting artifact.</summary>
    Run,
    /// <summary>Compile and run tests.</summary>
    Test,
    /// <summary>Install the bundled Mist agent references into .mist/agents/ and point the project's AGENTS.md at them.</summary>
    Steering,
    /// <summary>Inspect a CLR type's projected and hidden members.</summary>
    Inspect,
    /// <summary>Resolve packages and framework references without building.</summary>
    Restore,
    /// <summary>Print the compiler version.</summary>
    Version,
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
    string? InspectTypeName = null,
    string Pipeline = "standard",
    TargetPlatform? Platform = null,
    string? RuntimeIdentifier = null);

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