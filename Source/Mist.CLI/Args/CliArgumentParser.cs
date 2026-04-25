namespace Mist.CLI.Args;

/// <summary>
/// Error produced during CLI argument parsing.
/// </summary>
public sealed record CliParseError(string Message);

/// <summary>
/// Result of CLI argument parsing.
/// </summary>
public sealed record CliParseResult(CliArguments? Arguments, IReadOnlyList<CliParseError> Errors);

/// <summary>
/// Hand-rolled argument parser for the Mist CLI.
/// Parses: <c>mist [source-files...] [--backend &lt;id&gt;] [--output &lt;dir&gt;]</c>
/// </summary>
public static class CliArgumentParser
{
    public static CliParseResult Parse(string[] args)
    {
        var sourceFiles = new List<string>();
        var errors = new List<CliParseError>();
        var backendId = "csharp";
        var outputDirectory = "./out";

        for(var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if(arg == "--backend")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError("Missing value for --backend flag."));
                }
                else
                {
                    i++;
                    backendId = args[i];
                }
            }
            else if(arg == "--output")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError("Missing value for --output flag."));
                }
                else
                {
                    i++;
                    outputDirectory = args[i];
                }
            }
            else if(arg.StartsWith("--"))
            {
                errors.Add(new CliParseError($"Unknown flag: {arg}"));
            }
            else
            {
                sourceFiles.Add(arg);
            }
        }

        return errors.Count > 0
            ? new CliParseResult(null, errors)
            : new CliParseResult(new CliArguments(sourceFiles, backendId, outputDirectory), errors)
            ;
    }
}
