using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI;

public static class CliHost
{
    public static int Run(string[] args, Func<CliArguments, DiagnosticOutput, int> dispatch, TextWriter stderr)
    {
        var parsed = CliArgumentParser.Parse(args);
        var arguments = parsed.Arguments;
        var output = (parsed.Diagnostics ?? new DiagnosticControls()).CreateOutput(stderr,
            arguments?.Command.ToString().ToLowerInvariant() ?? "build", arguments?.ProjectFolder ?? "");
        if(parsed.DiagnosticError is { } reason)
        {
            var bag = new DiagnosticBag(output.Operation);
            bag.Admit(DiagnosticFactory.Create(HostDiagnostics.InvalidDiagnosticControl,
                new Absent(AbsenceReason.Unavailable), PipelineStage.Driver, [new TextValue(reason)]));
            output.Collect(bag.Admitted);
            output.Complete(false, outcome: "usageError");
            return 1;
        }

        if(parsed.Errors.Count > 0)
        {
            foreach(var error in parsed.Errors)
            {
                stderr.WriteLine($"error: {error.Message}");
            }

            CliHelp.Write(stderr);
            output.Complete(false, outcome: "usageError");
            return 1;
        }

        var exit = dispatch(arguments!, output);
        if(arguments!.Command is not (CliCommand.None or CliCommand.Version or CliCommand.Steering))
        {
            output.Complete(exit == 0);
        }

        return output.HasErrors ? 1 : exit;
    }
}