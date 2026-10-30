using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.CLI;
using Mist.CLI.Args;
using Mist.CLI.Layout;
using Mist.CLI.Output;
using Mist.CLI.Steering;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;

internal partial class Program
{
    private static int Main(string[] args)
    {
        return CliHost.Run(args, Dispatch, Console.Error,
            initialize: () => Console.OutputEncoding = System.Text.Encoding.UTF8);
    }

    private static int Dispatch(CliArguments cliArgs, DiagnosticOutput output)
    {
        switch(cliArgs.Command)
        {
            case CliCommand.None:
                CliHelp.Write(Console.Out);
                return 0;
            case CliCommand.Version:
                Console.WriteLine("mist 0.318.0");
                return 0;
            case CliCommand.Build: return RunBuildPipeline(cliArgs, output);
            case CliCommand.Run: return RunRunCommand(cliArgs, output);
            case CliCommand.Test: return RunTestCommand(cliArgs, output);
            case CliCommand.Steering: return RunSteeringCommand(cliArgs, output);
            case CliCommand.Inspect: return RunInspectCommand(cliArgs, output);
            case CliCommand.Restore: return RunRestoreCommand(cliArgs, output);
            default: return 1;
        }
    }

    private static int RunBuildPipeline(CliArguments cliArgs, DiagnosticOutput output)
    {
        ICompilationProgressReporter reporter = cliArgs.Quiet
            ? NullProgressReporter.Instance
            : new ConsoleProgressReporter(Console.Out);
        var totalStopwatch = Stopwatch.StartNew();

        var layout = LayoutOf(cliArgs);
        var sourceFiles = DiscoverSourcesAndClean(layout, reporter, output);
        if(sourceFiles is null)
        {
            return 1;
        }

        return cliArgs.Mode switch
        {
            CompilerMode.Lex => RunLexMode(sourceFiles, layout, output),
            _ => CompileThen(sourceFiles, cliArgs, output, reporter, totalStopwatch, _ => 0),
        };
    }

    private static ProjectLayout LayoutOf(CliArguments cliArgs)
    {
        return new ProjectLayout(cliArgs.ProjectFolder, cliArgs.OutputDirectory, cliArgs.BinDirectory, Directory.GetCurrentDirectory());
    }

    private static IReadOnlyList<string>? DiscoverSourcesAndClean(ProjectLayout layout, ICompilationProgressReporter reporter, DiagnosticOutput output)
    {
        if(layout.DirectoryHoldingProject() is { } holdingDirectory)
        {
            CommandFeedback.Report(output, CommandDiagnostics.UnsafeCleanDirectory, new ProjectLocation(Path.Combine(layout.ProjectFolder, "mist.toml"), null),
                new PathValue(holdingDirectory), new PathValue(layout.ProjectFolder));
            return null;
        }

        var sourceFiles = layout.DiscoverSources();

        if(sourceFiles.Count == 0)
        {
            CommandFeedback.Report(output, CommandDiagnostics.SourceFilesMissing, new ProjectLocation(Path.Combine(layout.ProjectFolder, "mist.toml"), null));
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
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            reporter.ReportStageComplete("0", cleanStopwatch.Elapsed, []);
            CommandFeedback.Report(output, CommandDiagnostics.CleanDirectoriesFailed, new ProjectLocation(Path.Combine(layout.ProjectFolder, "mist.toml"), null), new TextValue(ex.Message));
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

    private static int RunLexMode(IReadOnlyList<string> sourceFiles, ProjectLayout layout, DiagnosticOutput output)
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
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                CommandFeedback.Report(output, DriverDiagnostics.FileError, new FileOnly(filePath), new PathValue(filePath), new TextValue(ex.Message));
                hasErrors = true;
                continue;
            }

            var sourceText = new Mist.Common.Source.SourceText(filePath, source);
            var diags = new DiagnosticBag(output.Operation);
            var tokenStream = lexer.Lex(sourceText, diags);

            hasErrors |= diags.HasErrors;
            foreach(var diagnostic in diags.Admitted)
            {
                output.Stream(diagnostic);
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
                var offset = 0;
                while(offset < Math.Min(source.Length, reconstructed.Length) && source[offset] == reconstructed[offset])
                {
                    offset++;
                }

                CommandFeedback.Report(output, CommandDiagnostics.LexerRoundTripFailed, new FileOnly(filePath),
                    new PathValue(filePath), new QuantityValue(source.Length), new QuantityValue(reconstructed.Length), new QuantityValue(offset),
                    new OptionalValue(SlotKind.Name, offset < source.Length ? new NameValue(Escape(source[offset])) : null),
                    new OptionalValue(SlotKind.Name, offset < reconstructed.Length ? new NameValue(Escape(reconstructed[offset])) : null));
            }

            Console.WriteLine($"Lexed {filePath} -> {artifacts.TokensPath}, {artifacts.SourcePath}");
        }

        return hasErrors ? 1 : 0;
    }

    private static int CompileThen(
        IReadOnlyList<string> sourceFiles,
        CliArguments cliArgs,
        DiagnosticOutput output,
        ICompilationProgressReporter reporter,
        Stopwatch totalStopwatch,
        Func<CompilerResult, int> afterSuccess,
        ProjectConfiguration? configuration = null,
        Func<CompilerResult, CompilerResult>? beforeCompletion = null)
    {
        return new CompileFeedback(reporter, output).Run(
            listener => CompileProject(sourceFiles, cliArgs, output, reporter, listener, configuration),
            totalStopwatch,
            afterSuccess,
            beforeCompletion);
    }

    private static CompilerResult CompileProject(
        IReadOnlyList<string> sourceFiles,
        CliArguments cliArgs,
        DiagnosticOutput output,
        ICompilationProgressReporter progressReporter,
        IResolutionDiagnosticListener resolutionListener,
        ProjectConfiguration? configuration = null)
    {
        // Parse the project file to get the project name
        var parseDiagnostics = new DiagnosticBag(output.Operation);
        var config = configuration ?? ProjectFileParser.ParseRoot(Path.GetFullPath(cliArgs.ProjectFolder), parseDiagnostics);

        output.Collect(parseDiagnostics.Admitted);
        if(config is null || parseDiagnostics.HasErrors)
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
            Platform: cliArgs.Platform,
            RuntimeIdentifier: cliArgs.RuntimeIdentifier)
        {
            DiagnosticOperation = output.Operation,
            Configuration = config,
        };

        var driver = new CompilerDriver(new MistLexer(), new MistParser(), progressReporter, resolutionListener);

        // Run compilation
        return driver.Compile(options);
    }

    private static int RunRunCommand(CliArguments cliArgs, DiagnosticOutput output)
    {
        var diagnostics = new DiagnosticBag(output.Operation);
        var configuration = ProjectFileParser.ParseRoot(Path.GetFullPath(cliArgs.ProjectFolder), diagnostics);
        output.Collect(diagnostics.Admitted);
        if(configuration is null || diagnostics.HasErrors)
        {
            return 1;
        }

        if(configuration.OutputType == ProjectOutputType.Library)
        {
            CommandFeedback.Report(output, CommandDiagnostics.RunLibraryProject, new ProjectLocation(Path.Combine(cliArgs.ProjectFolder, "mist.toml"), null));
            return 1;
        }

        // For `run`, timing/progress output is hidden by default (clean stdout for the program).
        // Use --verbose to see it. --quiet still suppresses everything.
        ICompilationProgressReporter reporter = cliArgs.Verbose
            ? new ConsoleProgressReporter(Console.Out)
            : NullProgressReporter.Instance;
        var totalStopwatch = Stopwatch.StartNew();

        var sourceFiles = DiscoverSourcesAndClean(LayoutOf(cliArgs), reporter, output);
        if(sourceFiles is null)
        {
            return 1;
        }

        return CompileThen(sourceFiles, cliArgs, output, reporter, totalStopwatch, result => RunBuiltArtifact(result, output), configuration, result => CommandFeedback.RequireArtifact(result, output));
    }

    private static int RunBuiltArtifact(CompilerResult result, DiagnosticOutput output)
    {
        return RunArtifact(result.PrimaryOutputArtifactPath!, output);
    }

    private static int RunSteeringCommand(CliArguments cliArgs, DiagnosticOutput output)
    {
        var projectFolder = Path.GetFullPath(cliArgs.ProjectFolder);
        var result = SteeringInstaller.Install(projectFolder);

        if(!result.Succeeded)
        {
            var bag = new DiagnosticBag(output.Operation);
            bag.Admit(result.FailureDiagnostic!.Value);
            output.Collect(bag.Admitted);
            return 1;
        }

        if(!cliArgs.Quiet)
        {
            Console.WriteLine($"Installed Mist agent references into {SteeringInstaller.ReferenceDirectory(projectFolder)}");

            foreach(var path in result.Installed)
            {
                Console.WriteLine($"  + {Path.GetFileName(path)}");
            }

            if(result.SkippedUserFiles.Count > 0)
            {
                Console.WriteLine($"Left {result.SkippedUserFiles.Count} of your own file(s) there untouched.");
            }

            foreach(var file in result.InstructionFiles)
            {
                var action = file.Outcome switch
                {
                    ManagedBlockOutcome.Created => "Created",
                    ManagedBlockOutcome.Appended => "Added the Mist block to",
                    ManagedBlockOutcome.Replaced => "Updated the Mist block in",
                    _ => "Mist block already current in",
                };
                Console.WriteLine($"{action} {Path.GetFileName(file.Path)}");
            }

            if(result.ClaudeMdAbsent)
            {
                Console.WriteLine("No CLAUDE.md found. If you use Claude Code, add a CLAUDE.md containing @AGENTS.md.");
            }
        }

        return 0;
    }

    private static int RunInspectCommand(CliArguments cliArgs, DiagnosticOutput output)
    {
        if(cliArgs.InspectTypeName is null)
        {
            CommandFeedback.Report(output, CommandDiagnostics.InspectTypeRequired, new Absent(AbsenceReason.Unavailable));
            return 1;
        }

        var projectFolder = Path.GetFullPath(cliArgs.ProjectFolder);
        var projectFilePath = Path.Combine(projectFolder, "mist.toml");

        if(!File.Exists(projectFilePath))
        {
            CommandFeedback.Report(output, DriverDiagnostics.ProjectFileNotFound, new ProjectLocation(projectFilePath, null), new PathValue(projectFilePath));
            return 1;
        }

        // Parse the project file
        var parseDiagnostics = new DiagnosticBag(output.Operation);
        var config = ProjectFileParser.ParseRoot(projectFolder, parseDiagnostics);

        foreach(var diagnostic in parseDiagnostics.Admitted)
        {
            output.Stream(diagnostic);
        }

        if(parseDiagnostics.HasErrors || config is null)
        {
            return 1;
        }

        var referenceDiagnostics = new DiagnosticBag(output.Operation);
        var references = ProjectReferenceLoader.Load(projectFolder, config.References, config.TargetFramework, referenceDiagnostics);
        foreach(var diagnostic in referenceDiagnostics.Admitted)
        {
            output.Stream(diagnostic);
        }

        if(referenceDiagnostics.HasErrors)
        {
            return 1;
        }

        // Resolve SDK + package + framework assemblies (same as a build would use)
        var resolutionDiagnostics = new DiagnosticBag(output.Operation);
        var feedback = new InspectFeedback(output.Stream);
        var packageResult = PackageResolver.ResolvePackages(
            config.Packages,
            config.FrameworkReferences,
            config.TargetFramework,
            resolutionDiagnostics,
            () => feedback.Report(resolutionDiagnostics.ToImmutableArray()));

        var frameworkResult = FrameworkReferenceResolver.Resolve(
            config.FrameworkReferences,
            config.TargetFramework,
            resolutionDiagnostics);

        feedback.Report(resolutionDiagnostics.ToImmutableArray());
        if(feedback.Failed)
        {
            return 1;
        }

        // Merge additional assembly paths from packages and framework references
        var additionalPaths = packageResult.AssemblyPaths
            .AddRange(frameworkResult.AssemblyPaths)
            .AddRange(references.ExternalAssemblies.Select(external => external.Path));

        // Use the TypeInspector to resolve and print the inspection report
        var result = TypeInspector.Inspect(
            config.TargetFramework,
            additionalPaths,
            cliArgs.InspectTypeName,
            Console.Out, output.Operation, output.Stream);

        return result;
    }

    private static int RunRestoreCommand(CliArguments cliArgs, DiagnosticOutput output)
    {
        var projectFolder = Path.GetFullPath(cliArgs.ProjectFolder);
        var projectFilePath = Path.Combine(projectFolder, "mist.toml");

        if(!File.Exists(projectFilePath))
        {
            CommandFeedback.Report(output, DriverDiagnostics.ProjectFileNotFound, new ProjectLocation(projectFilePath, null), new PathValue(projectFilePath));
            return 1;
        }

        // Parse the project file
        var parseDiagnostics = new DiagnosticBag(output.Operation);
        var config = ProjectFileParser.ParseRoot(projectFolder, parseDiagnostics);

        return DependencyRestore.Run(projectFolder, config, parseDiagnostics.ToImmutableArray(), Console.Out, Console.Error, output.Operation, output.Stream);
    }

    private static int RunArtifact(string artifactPath, DiagnosticOutput output)
    {
        return ChildProcessRunner.Run(new ProcessStartInfo("dotnet", [artifactPath]) { UseShellExecute = false }, output);
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

}