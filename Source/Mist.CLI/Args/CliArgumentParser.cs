using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Args;

public sealed record CliParseError(MistDiagnostic Descriptor, ImmutableArray<SlotValue> Arguments)
{
    public string Message => DiagnosticRenderer.Render(Descriptor, Arguments);
}

public sealed record CliParseResult(CliArguments? Arguments, IReadOnlyList<CliParseError> Errors, DiagnosticControls? Diagnostics = null, string? DiagnosticError = null);

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
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--backend")]));
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
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--output")]));
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
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--bin")]));
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
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--mode")]));
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
                        errors.Add(new CliParseError(CommandDiagnostics.UnknownMode, [new NameValue(args[i])]));
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
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--pipeline")]));
                }
                else
                {
                    i++;
                    pipeline = args[i].ToLowerInvariant();
                    if(pipeline is not "standard")
                    {
                        errors.Add(new CliParseError(CommandDiagnostics.UnknownPipeline, [new NameValue(args[i])]));
                    }
                }
            }
            else if(arg == "--platform")
            {
                if(i + 1 >= args.Length)
                {
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--platform")]));
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
                        errors.Add(new CliParseError(CommandDiagnostics.UnknownPlatform, [new NameValue(args[i])]));
                    }
                }
            }
            else if(arg == "--rid")
            {
                i++;
                if(i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                {
                    errors.Add(new CliParseError(CommandDiagnostics.MissingFlagValue, [new NameValue("--rid")]));
                }
                else
                {
                    runtimeIdentifier = args[i];
                }
            }
            else if(arg.StartsWith("--"))
            {
                errors.Add(new CliParseError(CommandDiagnostics.UnknownFlag, [new NameValue(arg)]));
            }
            else
            {
                positionalArgs.Add(arg);
            }
        }

        if(verbose && quiet)
        {
            errors.Add(new CliParseError(CommandDiagnostics.ConflictingVerbosity, []));
        }

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

        if(command == CliCommand.Run && mode == CompilerMode.Lex)
        {
            errors.Add(new CliParseError(CommandDiagnostics.LexCommandIncompatible, [new EnumValue("run")]));
        }

        if(command == CliCommand.Test && mode == CompilerMode.Lex)
        {
            errors.Add(new CliParseError(CommandDiagnostics.LexCommandIncompatible, [new EnumValue("test")]));
        }

        if(command == CliCommand.None && positionalArgs.Count > 0)
        {
            command = CliCommand.Build;
        }

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

        string? projectFolder = null;

        if(positionalArgs.Count > 1)
        {
            errors.Add(new CliParseError(CommandDiagnostics.MultipleProjectFolders, []));
        }
        else if(positionalArgs.Count == 1)
        {
            var arg = positionalArgs[0];

            if(File.Exists(arg))
            {
                errors.Add(new CliParseError(CommandDiagnostics.IndividualFileUnsupported, []));
            }
            else if(!Directory.Exists(arg))
            {
                errors.Add(new CliParseError(CommandDiagnostics.ProjectDirectoryMissing, [new PathValue(arg)]));
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