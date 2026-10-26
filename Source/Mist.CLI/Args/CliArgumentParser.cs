using Mist.Driver.Contracts;

namespace Mist.CLI.Args;

/// <summary>
/// Error produced during CLI argument parsing.
/// </summary>
public sealed record CliParseError(string Message);

/// <summary>
/// Result of CLI argument parsing.
/// </summary>
public sealed record CliParseResult(CliArguments? Arguments, IReadOnlyList<CliParseError> Errors, DiagnosticControls? Diagnostics = null, string? DiagnosticError = null);

/// <summary>
/// Hand-rolled argument parser for the Mist CLI.
/// Parses: <c>mist &lt;build|run|test|steering|inspect|restore&gt; [project-folder] [--backend &lt;id&gt;] [--output &lt;dir&gt;] [--bin &lt;dir&gt;] [--mode &lt;compile|lex&gt;] [--quiet] [--verbose]</c>
/// </summary>
public static class CliArgumentParser
{
    public static CliParseResult Parse(string[] args)
    {
        var controls = DiagnosticControlParser.Parse(args);
        var result = ParseCore([.. controls.Remaining]);
        var command = result.Arguments?.Command ?? CliCommand.Build;
        var mode = result.Arguments?.Mode ?? CompilerMode.Compile;
        var error = DiagnosticControlParser.ValidateCommand(controls, command, mode);
        return result with
        {
            Arguments = result.Arguments is { } arguments ? arguments with { Diagnostics = controls.Controls } : null,
            Diagnostics = controls.Controls,
            DiagnosticError = error,
        };
    }

    private static CliParseResult ParseCore(string[] args)
    {
        var positionalArgs = new List<string>();
        var errors = new List<CliParseError>();
        var backendId = "csharp";
        var outputDirectory = "";
        var binDirectory = "";
        var mode = CompilerMode.Compile;
        var quiet = false;
        var verbose = false;
        var pipeline = "standard";
        TargetPlatform? platform = null;
        string? runtimeIdentifier = null;

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
            else if(arg is "--quiet" or "-q")
            {
                quiet = true;
            }
            else if(arg is "--verbose" or "-v")
            {
                verbose = true;
            }
            else if(arg == "--pipeline")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError("Missing value for --pipeline flag."));
                }
                else
                {
                    i++;
                    pipeline = args[i].ToLowerInvariant();
                    if(pipeline is not "standard")
                    {
                        errors.Add(new CliParseError($"Unknown pipeline: '{args[i]}'. Valid pipelines: standard"));
                    }
                }
            }
            else if(arg == "--platform")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError("Missing value for --platform flag."));
                }
                else
                {
                    i++;
                    platform = args[i].ToLowerInvariant() switch
                    {
                        "windows" => TargetPlatform.Windows,
                        "linux" => TargetPlatform.Linux,
                        "macos" => TargetPlatform.MacOS,
                        _ => null,
                    };
                    if(platform is null)
                    {
                        errors.Add(new CliParseError($"Unknown platform: '{args[i]}'. Valid platforms: windows, linux, macos"));
                    }
                }
            }
            else if(arg == "--rid")
            {
                i++;
                if(i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                {
                    errors.Add(new CliParseError("Missing value for --rid flag."));
                }
                else
                {
                    runtimeIdentifier = args[i];
                }
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

        // Detect command verb from the first positional argument.
        // If no positional arguments exist and no flags were provided,
        // the command stays None (triggers help output).
        var command = CliCommand.None;
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
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "test")
        {
            command = CliCommand.Test;
            positionalArgs.RemoveAt(0);
        }
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "restore")
        {
            command = CliCommand.Restore;
            positionalArgs.RemoveAt(0);
        }
        else if(positionalArgs.Count > 0 && positionalArgs[0] == "version")
        {
            command = CliCommand.Version;
            positionalArgs.RemoveAt(0);
        }

        // Validate run + lex mode combination
        if(command == CliCommand.Run && mode == CompilerMode.Lex)
        {
            errors.Add(new CliParseError("The 'run' command is not compatible with '--mode lex'."));
        }

        // Validate test + lex mode combination
        if(command == CliCommand.Test && mode == CompilerMode.Lex)
        {
            errors.Add(new CliParseError("The 'test' command is not compatible with '--mode lex'."));
        }

        // If no explicit command verb was given but there are positional args
        // (a project folder path), treat it as an implicit `build` command.
        if(command == CliCommand.None && positionalArgs.Count > 0)
        {
            command = CliCommand.Build;
        }

        // Version command needs no project folder or further validation.
        if(command == CliCommand.Version)
        {
            if(errors.Count > 0)
            {
                return new CliParseResult(null, errors);
            }

            return new CliParseResult(
                new CliArguments("", Command: command),
                errors);
        }

        // None command (bare `mist` with no args) needs no project folder.
        if(command == CliCommand.None)
        {
            if(errors.Count > 0)
            {
                return new CliParseResult(null, errors);
            }

            return new CliParseResult(
                new CliArguments("", Command: command),
                errors);
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
            new CliArguments(projectFolder!, Command: command, backendId, outputDirectory, binDirectory, mode, quiet, verbose, inspectTypeName, pipeline, platform, runtimeIdentifier),
            errors);
    }
}