using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Output;

public static class CommandFeedback
{
    public static void Report(DiagnosticOutput output, MistDiagnostic descriptor, Location location, params ReadOnlySpan<SlotValue> arguments)
    {
        var bag = new DiagnosticBag(output.Operation);
        bag.Admit(DiagnosticFactory.Create(descriptor, location, PipelineStage.Driver, arguments));
        foreach(var diagnostic in bag.Admitted)
        {
            output.ReportHostFailure(diagnostic);
            output.Stream(diagnostic);
        }
    }

    public static CompilerResult RequireArtifact(CompilerResult result, DiagnosticOutput output)
    {
        if(!result.Succeeded || result.PrimaryOutputArtifactPath is not null)
        {
            return result;
        }

        var bag = new DiagnosticBag(output.Operation);
        bag.ReportTyped(CommandDiagnostics.ExecutableArtifactMissing, new Absent(AbsenceReason.Unavailable));
        return result with { Succeeded = false, Diagnostics = result.Diagnostics.AddRange(bag.Admitted) };
    }
}