namespace Audit;

public enum Command
{
    Enumerate,
    Audit,
    Remediate,
    Report,
    Status,
}

public sealed record ParsedCommand(
    Command Command,
    string? FilePath,
    int TimeoutSeconds = 120);

public static class CommandParser
{
    /// <summary>
    /// Parses CLI arguments into a structured command. Writes errors to errorOutput and returns null on failure.
    /// </summary>
    public static ParsedCommand? Parse(string[] args, TextWriter errorOutput)
    {
        if(args.Length == 0)
        {
            WriteUsage(errorOutput);
            return null;
        }

        if(!TryParseCommand(args[0], out var command))
        {
            errorOutput.WriteLine($"Error: Unrecognized command '{args[0]}'.");
            errorOutput.WriteLine();
            WriteUsage(errorOutput);
            return null;
        }

        string? filePath = null;
        var timeoutSeconds = 120;

        for(var i = 1; i < args.Length; i++)
        {
            switch(args[i])
            {
                case "--file":
                    if(!SupportsFileOption(command))
                    {
                        errorOutput.WriteLine($"Error: --file is not supported for the '{args[0]}' command.");
                        return null;
                    }

                    if(i + 1 >= args.Length)
                    {
                        errorOutput.WriteLine("Error: --file requires a path argument.");
                        return null;
                    }

                    filePath = args[++i];
                    break;

                case "--timeout":
                    if(!SupportsTimeoutOption(command))
                    {
                        errorOutput.WriteLine($"Error: --timeout is not supported for the '{args[0]}' command.");
                        return null;
                    }

                    if(i + 1 >= args.Length)
                    {
                        errorOutput.WriteLine("Error: --timeout requires a seconds argument.");
                        return null;
                    }

                    if(!int.TryParse(args[++i], out timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        errorOutput.WriteLine("Error: --timeout value must be a positive integer.");
                        return null;
                    }

                    break;

                default:
                    errorOutput.WriteLine($"Error: Unrecognized option '{args[i]}'.");
                    errorOutput.WriteLine();
                    WriteUsage(errorOutput);
                    return null;
            }
        }

        return new ParsedCommand(command, filePath, timeoutSeconds);
    }

    private static bool TryParseCommand(string value, out Command command)
    {
        command = value.ToLowerInvariant() switch
        {
            "enumerate" => Command.Enumerate,
            "audit" => Command.Audit,
            "remediate" => Command.Remediate,
            "report" => Command.Report,
            "status" => Command.Status,
            _ => (Command)(-1),
        };

        return (int)command >= 0;
    }

    private static bool SupportsFileOption(Command command)
    {
        return command is Command.Audit or Command.Remediate;
    }

    private static bool SupportsTimeoutOption(Command command)
    {
        return command is Command.Audit or Command.Remediate;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: mist-audit <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  enumerate    Discover and register source files");
        output.WriteLine("  audit        Run AI-assisted code audit");
        output.WriteLine("  remediate    Apply AI-suggested remediations");
        output.WriteLine("  report       Generate AUDIT.md report");
        output.WriteLine("  status       Show audit progress summary");
        output.WriteLine();
        output.WriteLine("Options (audit, remediate only):");
        output.WriteLine("  --file <path>       Target a specific file");
        output.WriteLine("  --timeout <seconds> Agent timeout (default: 120)");
    }
}