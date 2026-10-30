using System.ComponentModel;
using System.Diagnostics;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI;

public static class ChildProcessRunner
{
    public static int Run(ProcessStartInfo startInfo, DiagnosticOutput output, Func<ProcessStartInfo, Process?>? start = null)
    {
        Process? child;
        try
        {
            child = (start ?? Process.Start)(startInfo);
        }
        catch(Exception exception) when(exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            CommandFeedback.Report(output, CommandDiagnostics.ProcessLaunchFailed, new Absent(AbsenceReason.Unavailable),
                new NameValue(startInfo.FileName), new TextValue(exception.Message));
            return 1;
        }

        if(child is null)
        {
            CommandFeedback.Report(output, CommandDiagnostics.ProcessStartFailed, new Absent(AbsenceReason.Unavailable),
                new NameValue(startInfo.FileName));
            return 1;
        }

        using(child)
        {
            child.WaitForExit();
            return child.ExitCode;
        }
    }
}