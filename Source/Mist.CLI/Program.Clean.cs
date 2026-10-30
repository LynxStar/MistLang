using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver.Generators;

internal partial class Program
{
    private static int RunCleanCommand(CliArguments arguments, DiagnosticOutput output)
    {
        try
        {
            if(arguments.Generators)
            {
                GeneratorCacheDirectory.Clean();
            }
            else
            {
                var layout = LayoutOf(arguments);
                if(layout.DirectoryHoldingProject() is { } unsafeDirectory)
                {
                    CommandFeedback.Report(output, CommandDiagnostics.UnsafeCleanDirectory,
                        new FileOnly(arguments.ProjectFolder), new PathValue(unsafeDirectory), new PathValue(arguments.ProjectFolder));
                    return 1;
                }

                CleanDirectory(layout.OutputDirectory);
                CleanDirectory(layout.BinDirectory);
            }

            return 0;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
        {
            CommandFeedback.Report(output, CommandDiagnostics.CleanDirectoriesFailed, new Absent(AbsenceReason.Unavailable),
                new TextValue("cache or output directories could not be removed"));
            return 1;
        }
    }
}