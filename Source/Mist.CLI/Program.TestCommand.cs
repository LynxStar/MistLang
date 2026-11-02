using System.Diagnostics;
using Mist.CLI;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

internal partial class Program
{
    private static int RunTestCommand(CliArguments cliArgs, DiagnosticOutput output)
    {
        var manifest = Path.Combine(Path.GetFullPath(cliArgs.ProjectFolder), "mist.toml");
        var parseDiagnostics = new DiagnosticBag(output.Operation);
        var configuration = ProjectFileParser.ParseRoot(Path.GetFullPath(cliArgs.ProjectFolder), parseDiagnostics);
        output.Collect(parseDiagnostics.Admitted);
        if(configuration is null || parseDiagnostics.HasErrors)
        {
            return 1;
        }

        ICompilationProgressReporter reporter = cliArgs.Verbose
            ? new ConsoleProgressReporter(Console.Out)
            : NullProgressReporter.Instance;
        var totalStopwatch = Stopwatch.StartNew();
        var sourceFiles = DiscoverSourcesAndClean(LayoutOf(cliArgs), cliArgs.BackendId, reporter, output);
        if(sourceFiles is null)
        {
            return 1;
        }

        return CompileThen(sourceFiles, cliArgs, output, reporter, totalStopwatch,
            _ => RunDotnetTest(Path.Combine(cliArgs.OutputDirectory, $"{configuration.Name}.csproj"), output),
            configuration,
            result => TestFrameworkWarning(result, sourceFiles, configuration, manifest, output));
    }

    private static CompilerResult TestFrameworkWarning(
        CompilerResult result, IReadOnlyList<string> sourceFiles, ProjectConfiguration configuration, string manifest, DiagnosticOutput output)
    {
        if(!result.Succeeded || !TestFrameworkDetection.ShouldWarn(ReadTestSources(sourceFiles), configuration.Packages))
        {
            return result;
        }

        var warnings = new DiagnosticBag(output.Operation);
        warnings.ReportTyped(DriverDiagnostics.TestFrameworkPackageMissing, new ProjectLocation(manifest, null));
        return result with { Diagnostics = result.Diagnostics.AddRange(warnings.Admitted) };
    }

    private static IEnumerable<string> ReadTestSources(IReadOnlyList<string> sourceFiles)
    {
        foreach(var file in sourceFiles)
        {
            string contents;
            try
            {
                contents = File.ReadAllText(file);
            }
            catch(IOException)
            {
                continue;
            }

            yield return contents;
        }
    }

    private static int RunDotnetTest(string csprojPath, DiagnosticOutput output)
    {
        return ChildProcessRunner.Run(new ProcessStartInfo("dotnet", ["test", csprojPath]) { UseShellExecute = false }, output);
    }
}