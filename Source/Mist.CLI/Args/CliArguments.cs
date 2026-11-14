using Mist.Driver.Contracts;

namespace Mist.CLI.Args;

public enum CliCommand
{
    /// <summary>No command provided — show help.</summary>
    None,
    Build,
    Run,
    Test,
    Steering,
    /// <summary>Inspect a CLR type's projected and hidden members.</summary>
    Inspect,
    /// <summary>Resolve packages and framework references without building.</summary>
    Restore,
    Version,
}

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
    string? RuntimeIdentifier = null,
    DiagnosticControls? Diagnostics = null);

public enum CompilerMode
{
    Compile,
    /// <summary>Lex only — dump token streams to output directory.</summary>
    Lex,
}