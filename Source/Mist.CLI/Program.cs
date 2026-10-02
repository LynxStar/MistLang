using System.Diagnostics;
using System.Text.RegularExpressions;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.CLI.Args;
using Mist.CLI.Layout;
using Mist.CLI.Output;
using Mist.CLI.Steering;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;

internal class Program
{
    private static int Main(string[] args)
    {
        // Ensure consistent UTF-8 output across all platforms. Without this,
        // Windows defaults to the system codepage (e.g., 1252) which mangles
        // Unicode characters in sample output and breaks cross-platform baselines.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Parse CLI arguments
        var parseResult = CliArgumentParser.Parse(args);

        if(parseResult.Errors.Count > 0)
        {
            foreach(var error in parseResult.Errors)
            {
                Console.Error.WriteLine($"error: {error.Message}");
            }

            Console.Error.WriteLine("Usage: mist <command> [project-folder] [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Commands:");
            Console.Error.WriteLine("  build              Compile the project");
            Console.Error.WriteLine("  run                Compile and execute the project");
            Console.Error.WriteLine("  test               Compile and run tests");
            Console.Error.WriteLine("  steering           Install Mist steering files");
            Console.Error.WriteLine("  inspect <TypeName> Inspect a CLR type's projected and hidden members");
            Console.Error.WriteLine("  restore            Resolve packages and framework references without building");
            Console.Error.WriteLine("  version            Print the compiler version");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --backend <id>     Backend to use (default: csharp)");
            Console.Error.WriteLine("  --output <dir>     Output directory for emitted files");
            Console.Error.WriteLine("  --bin <dir>        Binary output directory");
            Console.Error.WriteLine("  --mode <mode>      Compiler mode: compile, lex");
            Console.Error.WriteLine("  --pipeline <name>  Compilation pipeline: standard (default: standard)");
            Console.Error.WriteLine("  --platform <os>    Platform to compile for: windows, linux, macos (default: this machine)");
            Console.Error.WriteLine("  --verbose, -v      Show detailed output");
            Console.Error.WriteLine("  --quiet, -q        Suppress progress output");
            return 1;
        }

        var cliArgs = parseResult.Arguments!;

        switch(cliArgs.Command)
        {
            case CliCommand.None:
                PrintHelp();
                return 0;

            case CliCommand.Version:
                Console.WriteLine("mist 0.318.0");
                return 0;

            case CliCommand.Build:
                return RunBuildPipeline(cliArgs);

            case CliCommand.Run:
                return RunRunCommand(cliArgs);

            case CliCommand.Test:
                return RunTestCommand(cliArgs);

            case CliCommand.Steering:
                return RunSteeringCommand(cliArgs);

            case CliCommand.Inspect:
                return RunInspectCommand(cliArgs);

            case CliCommand.Restore:
                return RunRestoreCommand(cliArgs);

            default:
                Console.Error.WriteLine($"error: Unknown command '{cliArgs.Command}'.");
                return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("The Mist programming language compiler");
        Console.WriteLine();
        Console.WriteLine("Usage: mist <command> [project-folder] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build              Compile the project");
        Console.WriteLine("  run                Compile and execute the project");
        Console.WriteLine("  test               Compile and run tests");
        Console.WriteLine("  steering           Install Mist steering files");
        Console.WriteLine("  inspect <TypeName> Inspect a CLR type's projected and hidden members");
        Console.WriteLine("  restore            Resolve packages and framework references without building");
        Console.WriteLine("  version            Print the compiler version");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --backend <id>     Backend to use (default: csharp)");
        Console.WriteLine("  --output <dir>     Output directory for emitted files");
        Console.WriteLine("  --bin <dir>        Binary output directory");
        Console.WriteLine("  --mode <mode>      Compiler mode: compile, lex");
        Console.WriteLine("  --pipeline <name>  Compilation pipeline: standard (default: standard)");
        Console.WriteLine("  --platform <os>    Platform to compile for: windows, linux, macos (default: this machine)");
        Console.WriteLine("  --verbose, -v      Show detailed output");
        Console.WriteLine("  --quiet, -q        Suppress progress output");
    }

    private static int RunBuildPipeline(CliArguments cliArgs)
    {
        ICompilationProgressReporter reporter = cliArgs.Quiet
            ? NullProgressReporter.Instance
            : new ConsoleProgressReporter(Console.Out);
        var totalStopwatch = Stopwatch.StartNew();

        var layout = LayoutOf(cliArgs);
        var sourceFiles = DiscoverSourcesAndClean(layout, reporter);
        if(sourceFiles is null)
        {
            return 1;
        }

        return cliArgs.Mode switch
        {
            CompilerMode.Lex => RunLexMode(sourceFiles, layout),
            _ => RunCompileMode(sourceFiles, cliArgs, reporter, totalStopwatch),
        };
    }

    private static ProjectLayout LayoutOf(CliArguments cliArgs)
    {
        return new ProjectLayout(cliArgs.ProjectFolder, cliArgs.OutputDirectory, cliArgs.BinDirectory, Directory.GetCurrentDirectory());
    }

    private static IReadOnlyList<string>? DiscoverSourcesAndClean(ProjectLayout layout, ICompilationProgressReporter reporter)
    {
        if(layout.DirectoryHoldingProject() is { } holdingDirectory)
        {
            Console.Error.WriteLine($"error: '{holdingDirectory}' is or contains the project folder '{layout.ProjectFolder}'; cleaning it would delete the project.");
            return null;
        }

        var sourceFiles = layout.DiscoverSources();

        if(sourceFiles.Count == 0)
        {
            Console.Error.WriteLine("error: No .mist source files found.");
            return null;
        }

        // Stage 0: Cleaning
        reporter.ReportStageStart("0", "Cleaning");
        var cleanStopwatch = Stopwatch.StartNew();

        try
        {
            CleanDirectory(layout.OutputDirectory);
            CleanDirectory(layout.BinDirectory);
        }
        catch(IOException ex)
        {
            reporter.ReportStageComplete("0", cleanStopwatch.Elapsed, []);
            Console.Error.WriteLine($"error: Failed to clean build directories: {ex.Message}");
            return null;
        }

        cleanStopwatch.Stop();
        reporter.ReportStageComplete("0", cleanStopwatch.Elapsed, new[]
        {
            $"Cleaned {layout.OutputDirectory}",
            $"Cleaned {layout.BinDirectory}",
        });

        return sourceFiles;
    }

    private static int RunLexMode(IReadOnlyList<string> sourceFiles, ProjectLayout layout)
    {
        var lexer = new MistLexer();
        var hasErrors = false;

        _ = Directory.CreateDirectory(Path.GetDirectoryName(layout.LexMarkerPath)!);
        File.WriteAllText(layout.LexMarkerPath, "Written by mist build --mode lex. Mist does not read the .mist files in this directory as sources.");

        foreach(var filePath in sourceFiles)
        {
            string source;
            try
            {
                source = File.ReadAllText(filePath);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"error: Failed to read '{filePath}': {ex.Message}");
                hasErrors = true;
                continue;
            }

            var sourceText = new Mist.Common.Source.SourceText(filePath, source);
            var diags = new DiagnosticBag();
            var tokenStream = lexer.Lex(sourceText, diags);

            if(diags.HasErrors)
            {
                hasErrors = true;
                foreach(var d in diags.ToImmutableArray())
                {
                    Console.Error.WriteLine(DiagnosticFormatter.Format(d));
                }
            }

            // Build the output: hidden tokens render as their literal content,
            // default-channel tokens render as [TokenKind: lexeme].
            var sb = new System.Text.StringBuilder();
            foreach(var token in tokenStream.AllTokens)
            {
                if(token.Kind == (int)Mist.Generated.TokenKind.EndOfFile)
                {
                    continue;
                }

                if(token.Channel == Storm.Runtime.TokenChannel.Hidden)
                {
                    _ = sb.Append(token.Lexeme);
                }
                else
                {
                    var kindName = ((Mist.Generated.TokenKind)token.Kind).ToString();
                    _ = sb.Append($"[{kindName}: {token.Lexeme}]");
                }
            }

            var artifacts = layout.LexArtifactsFor(filePath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(artifacts.SourcePath)!);
            File.WriteAllText(artifacts.TokensPath, sb.ToString());

            // Reconstruct source from token lexemes and write .mist file
            var reconstructed = string.Concat(
                tokenStream.AllTokens
                    .Where(t => t.Kind != (int)Mist.Generated.TokenKind.EndOfFile)
                    .Select(t => t.Lexeme));

            File.WriteAllText(artifacts.SourcePath, reconstructed);

            // Round-trip check: reconstructed must match original
            if(reconstructed != source)
            {
                hasErrors = true;
                Console.Error.WriteLine($"error: Lexer round-trip failed for '{filePath}' — reconstructed source differs from original.");

                // Find first difference for debugging
                var minLen = Math.Min(source.Length, reconstructed.Length);
                for(var i = 0; i < minLen; i++)
                {
                    if(source[i] != reconstructed[i])
                    {
                        Console.Error.WriteLine($"  First difference at offset {i}: expected '{Escape(source[i])}' got '{Escape(reconstructed[i])}'");
                        break;
                    }
                }

                if(source.Length != reconstructed.Length)
                {
                    Console.Error.WriteLine($"  Length mismatch: original={source.Length} reconstructed={reconstructed.Length}");
                }
            }

            Console.WriteLine($"Lexed {filePath} -> {artifacts.TokensPath}, {artifacts.SourcePath}");
        }

        return hasErrors ? 1 : 0;
    }

    private static int RunCompileMode(IReadOnlyList<string> sourceFiles, CliArguments cliArgs, ICompilationProgressReporter reporter, Stopwatch totalStopwatch)
    {
        var result = CompileProject(sourceFiles, cliArgs, reporter);

        // Compute error/warning counts from diagnostics
        var errorCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warningCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

        totalStopwatch.Stop();
        reporter.ReportBuildSummary(result.Succeeded, errorCount, warningCount, totalStopwatch.Elapsed);

        // Format diagnostics to stderr
        foreach(var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        // Exit code
        return result.Succeeded ? 0 : 1;
    }

    private static CompilerResult CompileProject(IReadOnlyList<string> sourceFiles, CliArguments cliArgs, ICompilationProgressReporter? progressReporter = null)
    {
        // Parse the project file to get the project name
        var parseDiagnostics = new DiagnosticBag();
        var config = ProjectFileParser.Parse(Path.GetFullPath(cliArgs.ProjectFolder), parseDiagnostics);

        if(config is null)
        {
            return new CompilerResult(false, null, parseDiagnostics.ToImmutableArray());
        }

        // Map CLI arguments to CompilerOptions
        var options = new CompilerOptions(
            ProjectFolder: Path.GetFullPath(cliArgs.ProjectFolder),
            SourcePaths: [.. sourceFiles],
            Backend: new BackendId(cliArgs.BackendId),
            OutputDirectory: cliArgs.OutputDirectory,
            BinDirectory: cliArgs.BinDirectory,
            ProjectName: config.Name,
            NamespaceRoot: config.NamespaceRoot,
            EntryPoint: config.EntryPoint,
            Packages: config.Packages,
            FrameworkReferences: config.FrameworkReferences,
            TargetFramework: config.TargetFramework,
            VerboseResolution: cliArgs.Verbose,
            CsprojProperties: config.CsprojProperties,
            OutputType: config.OutputType,
            Pipeline: cliArgs.Pipeline switch
            {
                "standard" => PipelineMode.Standard,
                _ => throw new InvalidOperationException($"Unknown pipeline: '{cliArgs.Pipeline}'."),
            },
            Platform: cliArgs.Platform);

        var driver = new CompilerDriver(new MistLexer(), new MistParser(), progressReporter);

        // Run compilation
        return driver.Compile(options);
    }

    private static int RunRunCommand(CliArguments cliArgs)
    {
        // Validate that the project is not a library before starting compilation.
        var runParseDiagnostics = new DiagnosticBag();
        var runConfig = ProjectFileParser.Parse(Path.GetFullPath(cliArgs.ProjectFolder), runParseDiagnostics);

        if(runConfig is null)
        {
            foreach(var diagnostic in runParseDiagnostics.ToImmutableArray())
            {
                Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            }

            return 1;
        }

        if(runConfig.OutputType == ProjectOutputType.Library)
        {
            Console.Error.WriteLine("error: 'mist run' can only be used with executable projects. This project has output_type = \"library\".");
            return 1;
        }

        // For `run`, timing/progress output is hidden by default (clean stdout for the program).
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

        // Compile the project
        var result = CompileProject(sourceFiles, cliArgs, reporter);

        // Compute error/warning counts from diagnostics
        var errorCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warningCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

        totalStopwatch.Stop();
        reporter.ReportBuildSummary(result.Succeeded, errorCount, warningCount, totalStopwatch.Elapsed);

        // On build failure, print diagnostics and return non-zero
        if(!result.Succeeded)
        {
            foreach(var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            }

            return 1;
        }

        // Print any non-error diagnostics (warnings, etc.)
        foreach(var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        // Check for artifact path
        if(result.PrimaryOutputArtifactPath is null)
        {
            Console.Error.WriteLine("error: Build succeeded but no executable artifact was produced.");
            return 1;
        }

        return RunArtifact(result.PrimaryOutputArtifactPath);
    }

    private static int RunSteeringCommand(CliArguments cliArgs)
    {
        var projectFolder = Path.GetFullPath(cliArgs.ProjectFolder);
        var result = SteeringInstaller.Install(projectFolder);

        if(!result.Succeeded)
        {
            Console.Error.WriteLine($"error: {result.Error}");
            return 1;
        }

        if(!cliArgs.Quiet)
        {
            var steeringDir = Path.Combine(projectFolder, ".kiro", "steering");
            Console.WriteLine($"Installed Mist steering files into {steeringDir}");

            foreach(var path in result.Installed)
            {
                Console.WriteLine($"  + {Path.GetFileName(path)}");
            }

            if(result.SkippedUserFiles.Count > 0)
            {
                Console.WriteLine($"Left {result.SkippedUserFiles.Count} of your own steering file(s) untouched.");
            }
        }

        return 0;
    }

    private static int RunInspectCommand(CliArguments cliArgs)
    {
        if(cliArgs.InspectTypeName is null)
        {
            Console.Error.WriteLine("Usage: mist inspect <TypeName>");
            Console.Error.WriteLine("  <TypeName> is the fully-qualified CLR type name to inspect.");
            return 1;
        }

        var projectFolder = Path.GetFullPath(cliArgs.ProjectFolder);
        var projectFilePath = Path.Combine(projectFolder, "mist.toml");

        if(!File.Exists(projectFilePath))
        {
            Console.Error.WriteLine("error: No mist.toml found in the current directory. Run this command from a Mist project folder.");
            return 1;
        }

        // Parse the project file
        var parseDiagnostics = new DiagnosticBag();
        var config = ProjectFileParser.Parse(projectFolder, parseDiagnostics);

        if(config is null)
        {
            foreach(var diagnostic in parseDiagnostics.ToImmutableArray())
            {
                Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            }

            return 1;
        }

        // Resolve SDK + package + framework assemblies (same as a build would use)
        var packageDiagnostics = new DiagnosticBag();
        var packageResult = PackageResolver.ResolvePackages(
            config.Packages,
            config.FrameworkReferences,
            config.TargetFramework,
            packageDiagnostics);

        var frameworkDiagnostics = new DiagnosticBag();
        var frameworkResult = FrameworkReferenceResolver.Resolve(
            config.FrameworkReferences,
            config.TargetFramework,
            frameworkDiagnostics);

        // Merge additional assembly paths from packages and framework references
        var additionalPaths = packageResult.AssemblyPaths
            .AddRange(frameworkResult.AssemblyPaths);

        // Use the TypeInspector to resolve and print the inspection report
        var result = TypeInspector.Inspect(
            config.TargetFramework,
            additionalPaths,
            cliArgs.InspectTypeName,
            Console.Out);

        return result;
    }

    private static int RunRestoreCommand(CliArguments cliArgs)
    {
        var projectFolder = Path.GetFullPath(cliArgs.ProjectFolder);
        var projectFilePath = Path.Combine(projectFolder, "mist.toml");

        if(!File.Exists(projectFilePath))
        {
            Console.Error.WriteLine("error: No mist.toml found in the current directory. Run this command from a Mist project folder.");
            return 1;
        }

        // Parse the project file
        var parseDiagnostics = new DiagnosticBag();
        var config = ProjectFileParser.Parse(projectFolder, parseDiagnostics);

        if(config is null)
        {
            foreach(var diagnostic in parseDiagnostics.ToImmutableArray())
            {
                Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            }

            return 1;
        }

        // Run package resolution
        var packageDiagnostics = new DiagnosticBag();
        var packageResult = PackageResolver.ResolvePackages(
            config.Packages,
            config.FrameworkReferences,
            config.TargetFramework,
            packageDiagnostics);

        // Run framework reference resolution
        var frameworkDiagnostics = new DiagnosticBag();
        var frameworkResult = FrameworkReferenceResolver.Resolve(
            config.FrameworkReferences,
            config.TargetFramework,
            frameworkDiagnostics);

        // Print per-package detail lines unconditionally
        foreach(var pkg in packageResult.Resolved)
        {
            Console.WriteLine($"Restored {pkg.Name} {pkg.Version} ({pkg.SelectedTfm})");
        }

        // Print per-framework detail lines unconditionally
        foreach(var fw in frameworkResult.Resolved)
        {
            Console.WriteLine($"Resolved {fw.Name} {fw.Version}");
        }

        // Print zero-assemblies notes for packages that resolved but contributed nothing
        foreach(var pkg in packageResult.Resolved)
        {
            if(pkg.AssemblyCount == 0)
            {
                Console.WriteLine($"{pkg.Name} contributed no assemblies for {config.TargetFramework}");
            }
        }

        // Print summary
        var successCount = packageResult.Resolved.Length + frameworkResult.Resolved.Length;
        var failureCount = packageResult.FailureCount + frameworkResult.FailureCount;
        Console.WriteLine($"{successCount} succeeded, {failureCount} failed");

        // Emit diagnostics on failure
        foreach(var diagnostic in packageDiagnostics.ToImmutableArray())
        {
            Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        foreach(var diagnostic in frameworkDiagnostics.ToImmutableArray())
        {
            Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        return failureCount > 0 ? 1 : 0;
    }

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

        // Compile the project
        var result = CompileProject(sourceFiles, cliArgs, reporter);

        // Compute error/warning counts from diagnostics
        var errorCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warningCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

        totalStopwatch.Stop();
        reporter.ReportBuildSummary(result.Succeeded, errorCount, warningCount, totalStopwatch.Elapsed);

        // On build failure, print diagnostics and return non-zero
        if(!result.Succeeded)
        {
            foreach(var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
            }

            return 1;
        }

        // Print any non-error diagnostics (warnings, etc.)
        foreach(var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

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

    private static int RunArtifact(string artifactPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = artifactPath,
                UseShellExecute = false,
            };

            var process = Process.Start(startInfo);
            if(process is null)
            {
                Console.Error.WriteLine("error: Failed to start 'dotnet' process.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"error: Failed to launch 'dotnet': {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Deletes all files and subdirectories within a directory. No-op if the directory does not exist.
    /// </summary>
    private static void CleanDirectory(string path)
    {
        if(!Directory.Exists(path))
        {
            return;
        }

        var di = new DirectoryInfo(path);
        foreach(var file in di.GetFiles())
        {
            file.Delete();
        }

        foreach(var dir in di.GetDirectories())
        {
            dir.Delete(true);
        }
    }

    private static string Escape(char c)
    {
        return c switch
        {
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\0' => "\\0",
            ' ' => "\\s",
            _ => c.ToString(),
        };
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