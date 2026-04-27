namespace Mist.CLI.Args;

/// <summary>
/// Parsed command-line arguments for the Mist compiler CLI.
/// </summary>
public sealed record CliArguments(
    IReadOnlyList<string> SourceFiles,
    string BackendId = "csharp",
    string OutputDirectory = "./out",
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
