using System.Collections.Immutable;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI;

public static class CliHost
{
    public static int Run(
        string[] args,
        Func<CliArguments, DiagnosticOutput, int> dispatch,
        TextWriter stderr,
        Func<string[], CliParseResult>? parse = null,
        Action? initialize = null)
    {
        var output = new DiagnosticOutput(stderr);
        try
        {
            initialize?.Invoke();
            var parsed = (parse ?? CliArgumentParser.Parse)(args);
            var arguments = parsed.Arguments;
            output = (parsed.Diagnostics ?? new DiagnosticControls()).CreateOutput(stderr,
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
                var bag = new DiagnosticBag(output.Operation);
                foreach(var error in parsed.Errors)
                {
                    bag.Admit(DiagnosticFactory.Create(error.Descriptor, new Absent(AbsenceReason.Unavailable),
                        PipelineStage.Driver, error.Arguments.AsSpan()));
                }

                foreach(var diagnostic in bag.Admitted)
                {
                    output.Stream(diagnostic);
                }

                if((parsed.Diagnostics?.Format ?? DiagnosticFormat.Text) == DiagnosticFormat.Text)
                {
                    CliHelp.Write(stderr);
                }

                output.Complete(false, outcome: "usageError");
                return 1;
            }

            var exit = dispatch(arguments!, output);
            if(arguments!.Command is not (CliCommand.None or CliCommand.Version))
            {
                output.Complete(exit == 0);
            }

            return output.HasErrors ? 1 : exit;
        }
        catch(Exception exception)
        {
            var details = ImmutableArray.CreateBuilder<SlotValue>();
            for(var cause = exception; cause is not null; cause = cause.InnerException)
            {
                details.Add(new TextValue(cause.GetType().FullName ?? cause.GetType().Name));
                details.Add(new TextValue(cause.Message));
                details.Add(new TextValue(cause.StackTrace ?? ""));
            }

            var bag = new DiagnosticBag(output.Operation);
            bag.Admit(DiagnosticFactory.Create(HostDiagnostics.UnhandledException,
                new Absent(AbsenceReason.Unavailable), PipelineStage.Driver,
                [new SequenceValue(SlotKind.Text, details.ToImmutable())]));
            foreach(var diagnostic in bag.Admitted)
            {
                output.ReportHostFailure(diagnostic);
            }

            output.Complete(false, outcome: "internalFailure");
            return 1;
        }
    }
}