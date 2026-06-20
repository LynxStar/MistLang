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
/// Parses: <c>mist &lt;build|run|steering|inspect|restore&gt; [project-folder] [--backend &lt;id&gt;] [--output &lt;dir&gt;] [--bin &lt;dir&gt;] [--mode &lt;compile|lex&gt;] [--quiet] [--verbose]</c>
/// </summary>
public static class CliArgumentParser
{
    public static CliParseResult Parse(string[] args)
    {
        var positionalArgs = new List<string>();
        var errors = new List<CliParseError>();
        var backendId = "csharp";
        var outputDirectory = "";
        var binDirectory = "";
        var mode = CompilerMode.Compile;
        var quiet = false;
        var verbose = false;

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
            else if(arg == "--bin")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError("Missing value for --bin flag."));
                }
                else
                {
                    i++;
                    binDirectory = args[i];
                }
            }
            else if(arg == "--mode")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError("Missing value for --mode flag."));
                }
                else
                {
                    i++;
                    mode = args[i].ToLowerInvariant() switch
                    {
                        "compile" => CompilerMode.Compile,
                        "lex" => CompilerMode.Lex,
                        _ => CompilerMode.Compile,
                    };
                    if(args[i].ToLowerInvariant() is not ("compile" or "lex"))
                    {
                        errors.Add(new CliParseError($"Unknown mode: '{args[i]}'. Valid modes: compile, lex"));
                    }
                }
            }
            else if(arg == "--quiet" || arg == "-q")
            {
                quiet = true;
            }
            else if(arg == "--verbose" || arg == "-v")
            {
                verbose = true;
            }
            else if(arg.StartsWith("--"))
            {
                errors.Add(new CliParseError($"Unknown flag: {arg}"));
            }
            else
            {
                positionalArgs.Add(arg);
            }
        }

        // --verbose and --quiet are mutually exclusive
        if(verbose && quiet)
        {
            errors.Add(new CliParseError("The '--verbose' and '--quiet' flags are mutually exclusive."));
        }

        // Detect command verb from the first positional argument
        var command = CliCommand.Build;
        string? inspectTypeName = null;

        if(positionalArgs.Count > 0 && positionalArgs[0] == "build")
        {
            command = CliCommand.Build;
            positionalArgs.RemoveAt(0);
        }
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "run")
        {
            command = CliCommand.Run;
            positionalArgs.RemoveAt(0);
        }
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "steering")
        {
            command = CliCommand.Steering;
            positionalArgs.RemoveAt(0);
        }
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "inspect")
        {
            command = CliCommand.Inspect;
            positionalArgs.RemoveAt(0);

            // Capture the next positional argument as the type name
            if(positionalArgs.Count > 0)
            {
                inspectTypeName = positionalArgs[0];
                positionalArgs.RemoveAt(0);
            }
        }
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "restore")
        {
            command = CliCommand.Restore;
            positionalArgs.RemoveAt(0);
        }

        // Validate run + lex mode combination
        if(command == CliCommand.Run && mode == CompilerMode.Lex)
        {
            errors.Add(new CliParseError("The 'run' command is not compatible with '--mode lex'."));
        }

        // Validate positional arguments
        string? projectFolder = null;

        if(positionalArgs.Count > 1)
        {
            errors.Add(new CliParseError("Only a single project folder is accepted."));
        }
        else if(positionalArgs.Count == 1)
        {
            var arg = positionalArgs[0];

            if(File.Exists(arg))
            {
                errors.Add(new CliParseError("Individual file paths are no longer accepted. Provide a project folder instead."));
            }
            else if(!Directory.Exists(arg))
            {
                errors.Add(new CliParseError($"Directory does not exist: '{arg}'."));
            }
            else
            {
                projectFolder = arg;
            }
        }
        else
        {
            projectFolder = Directory.GetCurrentDirectory();
        }

        if(errors.Count > 0)
        {
            return new CliParseResult(null, errors);
        }

        // Resolve default directories
        if(string.IsNullOrEmpty(outputDirectory))
        {
            outputDirectory = Path.Combine(projectFolder!, "out");
        }

        if(string.IsNullOrEmpty(binDirectory))
        {
            binDirectory = Path.Combine(projectFolder!, "bin");
        }

        return new CliParseResult(
            new CliArguments(projectFolder!, Command: command, backendId, outputDirectory, binDirectory, mode, quiet, verbose, inspectTypeName),
            errors);
    }
}
