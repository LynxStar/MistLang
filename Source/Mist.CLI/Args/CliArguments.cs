namespace Mist.CLI.Args;

/// <summary>
/// Parsed command-line arguments for the Mist compiler CLI.
/// </summary>
public sealed record CliArguments(IReadOnlyList<string> SourceFiles, string BackendId = "csharp", string OutputDirectory = "./out");
