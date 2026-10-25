using System.Diagnostics;
using System.Text.RegularExpressions;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

internal partial class Program
{
    private static int RunTestCommand(CliArguments cliArgs)
    {
        // For `test`, timing/progress output is hidden by default (clean stdout for test output).
        // Use --verbose to see it. --quiet still suppresses everything.
        ICompilationProgressReporter reporter = cliArgs.Verbose
            ? new ConsoleProgressReporter(Console.Out)
            : NullProgressReporter.Instance;
        var totalStopwatch = Stopwatch.StartNew();

        var sourceFiles = DiscoverSourcesAndClean(LayoutOf(cliArgs), reporter);
        if(sourceFiles is null)
        {
            return 1;
        }

        return CompileThen(sourceFiles, cliArgs, reporter, totalStopwatch, _ => RunBuiltTests(sourceFiles, cliArgs));
    }

    private static int RunBuiltTests(IReadOnlyList<string> sourceFiles, CliArguments cliArgs)
    {
        // Determine the .csproj path in the output directory
        var parseDiagnostics = new DiagnosticBag();
        var config = ProjectFileParser.Parse(Path.GetFullPath(cliArgs.ProjectFolder), parseDiagnostics);
        var csprojPath = Path.Combine(cliArgs.OutputDirectory, $"{config!.Name}.csproj");

        // Check for test-attributed methods without test framework packages (MIST_TEST_0001).
        if(HasTestAttributedMethods(sourceFiles) && !HasTestFrameworkPackage(config))
        {
            var warning = new Diagnostic(
                new DiagnosticId("MIST_TEST_0001"),
                DiagnosticSeverity.Warning,
                PipelineStage.Driver,
                "Test-attributed methods found but no test framework package is referenced. Add xUnit, NUnit, or MSTest packages to [packages] in mist.toml.",
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null);
            Console.Error.WriteLine(DiagnosticFormatter.Format(warning));
        }

        return RunDotnetTest(csprojPath);
    }

    private static int RunDotnetTest(string csprojPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{csprojPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var process = Process.Start(startInfo);
            if(process is null)
            {
                Console.Error.WriteLine("error: Failed to start 'dotnet' process.");
                return 1;
            }

            // Stream stdout and stderr in real time using async reads
            var stdoutTask = Task.Run(() =>
            {
                while(process.StandardOutput.ReadLine() is { } line)
                {
                    Console.WriteLine(line);
                }
            });

            var stderrTask = Task.Run(() =>
            {
                while(process.StandardError.ReadLine() is { } line)
                {
                    Console.Error.WriteLine(line);
                }
            });

            process.WaitForExit();
            stdoutTask.Wait();
            stderrTask.Wait();

            return process.ExitCode;
        }
        catch(System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("error: Failed to start 'dotnet' process.");
            return 1;
        }
    }

    /// <summary>
    /// Scans source files for method declarations that have attributes whose unqualified
    /// name ends in <c>Fact</c>, <c>Theory</c>, <c>Test</c>, or <c>TestMethod</c>.
    /// Uses a regex-based heuristic over the source text since the compiled AST is not
    /// retained in <see cref="CompilerResult"/>.
    /// </summary>
    private static bool HasTestAttributedMethods(IReadOnlyList<string> sourceFiles)
    {
        // Match attribute syntax: [SomeName] where SomeName ends with Fact, Theory, Test, or TestMethod.
        // The pattern accounts for optional qualifiers (e.g., [Xunit.Fact]) and optional arguments.
        var pattern = new Regex(
            @"\[\s*[\w.]*(?:Fact|Theory|Test|TestMethod)\s*[\(\]]",
            RegexOptions.Compiled);

        foreach(var file in sourceFiles)
        {
            try
            {
                var contents = File.ReadAllText(file);
                if(pattern.IsMatch(contents))
                {
                    return true;
                }
            }
            catch(IOException)
            {
                // If we can't read the file, skip it.
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if any package in the project configuration has a name
    /// containing "Test" or "xunit" (case-insensitive substring match).
    /// </summary>

    /// <summary>
    /// Returns <c>true</c> if any package in the project configuration has a name
    /// containing "Test" or "xunit" (case-insensitive substring match).
    /// </summary>
    private static bool HasTestFrameworkPackage(ProjectConfiguration config)
    {
        if(config.Packages.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach(var package in config.Packages)
        {
            if(package.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                || package.Name.Contains("xunit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}