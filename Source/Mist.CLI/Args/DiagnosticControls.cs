using System.Collections.Immutable;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Args;

public sealed record DiagnosticControls(
    DiagnosticFormat Format = DiagnosticFormat.Text,
    string? File = null,
    DiagnosticSeverity Minimum = DiagnosticSeverity.Info,
    DiagnosticPolicyOverrides? Policy = null)
{
    public DiagnosticOutput CreateOutput(TextWriter error, string command, string project, bool verbose = false)
    {
        var output = new DiagnosticOutput(error, Format, Minimum, command, project,
            File is { } path ? bytes => System.IO.File.WriteAllBytes(path, bytes) : null, verbose);
        output.Operation.Overrides = Policy ?? new DiagnosticPolicyOverrides();
        output.Operation.Policy = output.Operation.Overrides.Apply(DiagnosticPolicy.Default);
        return output;
    }
}

public sealed record DiagnosticControlParse(ImmutableArray<string> Remaining, DiagnosticControls Controls, string? Error, bool HasControls, bool HasPolicy);

public static class DiagnosticControlParser
{
    public static DiagnosticControlParse Parse(string[] args, Func<string, MistDiagnostic?>? find = null)
    {
        find ??= DiagnosticRegistry.Index.Find;
        var controls = new DiagnosticControls();
        var remaining = new List<string>();
        string? error = null;
        var hasControls = false;
        var hasPolicy = false;
        for(var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if(flag is not ("--diagnostic-format" or "--diagnostic-file" or "--min-severity" or "--warnings-as-errors" or "--disable" or "--enable"))
            {
                remaining.Add(flag);
                continue;
            }

            hasControls = true;
            hasPolicy |= flag is "--warnings-as-errors" or "--disable" or "--enable";
            string? value = null;
            if(i + 1 < args.Length && !args[i + 1].StartsWith('-')
                && !(flag == "--warnings-as-errors" && args[i + 1] is "build" or "run" or "test" or "inspect" or "restore" or "steering" or "version"))
            {
                value = args[++i];
            }

            if(flag == "--warnings-as-errors" && value is null)
            {
                controls = controls with { Policy = (controls.Policy ?? new()) with { PromoteAll = true, Promoted = [] } };
                continue;
            }

            if(string.IsNullOrWhiteSpace(value))
            {
                error ??= $"Missing value for {flag}.";
                continue;
            }

            switch(flag)
            {
                case "--diagnostic-format":
                    if(value is "text" or "json")
                    {
                        controls = controls with { Format = value == "json" ? DiagnosticFormat.Json : DiagnosticFormat.Text };
                    }
                    else
                    {
                        error ??= $"Invalid diagnostic format '{value}'; expected text or json.";
                    }

                    break;
                case "--diagnostic-file":
                    if(value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    {
                        error ??= "Invalid diagnostic file path.";
                    }
                    else
                    {
                        controls = controls with { File = value };
                    }

                    break;
                case "--min-severity":
                    if(value is "info" or "warning" or "error")
                    {
                        controls = controls with { Minimum = value switch { "error" => DiagnosticSeverity.Error, "warning" => DiagnosticSeverity.Warning, _ => DiagnosticSeverity.Info } };
                    }
                    else
                    {
                        error ??= $"Invalid minimum severity '{value}'; expected info, warning or error.";
                    }

                    break;
                default:
                    var numbers = ImmutableHashSet.CreateBuilder<DiagnosticNumber>();
                    foreach(var number in value.Split(','))
                    {
                        if(find(number) is not { } descriptor)
                        {
                            error ??= $"Unknown diagnostic number '{number}' in {flag}.";
                        }
                        else if(flag == "--warnings-as-errors" ? descriptor.Policy == PolicyClass.Fixed : descriptor.Policy != PolicyClass.Optional)
                        {
                            error ??= $"Diagnostic '{number}' has policy {descriptor.Policy} and cannot be used with {flag}.";
                        }
                        else
                        {
                            _ = numbers.Add(descriptor.Number);
                        }
                    }

                    var policy = controls.Policy ?? new DiagnosticPolicyOverrides();
                    controls = controls with
                    {
                        Policy = flag switch
                        {
                            "--warnings-as-errors" => policy with { PromoteAll = false, Promoted = numbers.ToImmutable() },
                            "--disable" => policy with { Disabled = (policy.Disabled ?? []).Union(numbers), Enabled = (policy.Enabled ?? []).Except(numbers) },
                            _ => policy with { Enabled = (policy.Enabled ?? []).Union(numbers), Disabled = (policy.Disabled ?? []).Except(numbers) },
                        },
                    };
                    break;
            }
        }

        return new DiagnosticControlParse([.. remaining], controls, error, hasControls, hasPolicy);
    }

    public static string? ValidateCommand(DiagnosticControlParse parsed, CliCommand command, CompilerMode mode)
    {
        if(parsed.HasControls && command is CliCommand.Steering or CliCommand.Version)
        {
            return $"Unknown diagnostic option for '{command.ToString().ToLowerInvariant()}'.";
        }

        return parsed.HasPolicy && mode == CompilerMode.Lex ? "Unknown policy option for lex mode." : parsed.Error;
    }
}