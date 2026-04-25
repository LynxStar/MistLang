namespace Mist.CLI.Args;

/// <summary>
/// Hand-rolled argument parser for the Mist CLI.
/// Parses: <c>mist [source-files...] [--backend &lt;id&gt;] [--output &lt;dir&gt;]</c>
/// </summary>
public static class CliArgumentParser
{
    public sealed record ParseError(string Message);

    public sealed record ParseResult(CliArguments? Arguments, IReadOnlyList<ParseError> Errors);

    public static ParseResult Parse(string[] args)
    {
        var sourceFiles = new List<string>();
        var errors = new List<ParseError>();
        string backendId = "csharp";
        string outputDirectory = "./out";

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--backend")
            {
                if (i + 1 >= args.Length)
                {
                    errors.Add(new ParseError("Missing value for --backend flag."));
                }
                else
                {
                    i++;
                    backendId = args[i];
                }
            }
            else if (arg == "--output")
            {
                if (i + 1 >= args.Length)
                {
                    errors.Add(new ParseError("Missing value for --output flag."));
                }
                else
                {
                    i++;
                    outputDirectory = args[i];
                }
            }
            else if (arg.StartsWith("--"))
            {
                errors.Add(new ParseError($"Unknown flag: {arg}"));
            }
            else
            {
                sourceFiles.Add(arg);
            }
        }

        if (errors.Count > 0)
        {
            return new ParseResult(null, errors);
        }

        return new ParseResult(
            new CliArguments(sourceFiles, backendId, outputDirectory),
            errors);
    }
}
