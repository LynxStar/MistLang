using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

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

            Console.Error.WriteLine("Usage: mist <build|run> [project-folder] [--backend <id>] [--output <dir>] [--bin <dir>] [--mode <compile|lex>]");
            return 1;
        }

        var cliArgs = parseResult.Arguments!;

        switch(cliArgs.Command)
        {
            case CliCommand.Build:
                return RunBuildPipeline(cliArgs);

            case CliCommand.Run:
                return RunRunCommand(cliArgs);

            default:
                Console.Error.WriteLine($"error: Unknown command '{cliArgs.Command}'.");
                return 1;
        }
    }

    private static int RunBuildPipeline(CliArguments cliArgs)
    {
        ICompilationProgressReporter reporter = cliArgs.Quiet
            ? NullProgressReporter.Instance
            : new ConsoleProgressReporter(Console.Out);
        var totalStopwatch = Stopwatch.StartNew();

        // Discover all .mist source files from the project folder, sorted ordinally.
        var sourceFiles = Directory.EnumerateFiles(cliArgs.ProjectFolder, "*.mist", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if(sourceFiles.Count == 0)
        {
            Console.Error.WriteLine("error: No .mist source files found.");
            return 1;
        }

        // Stage 0: Cleaning
        reporter.ReportStageStart(0, "Cleaning");
        var cleanStopwatch = Stopwatch.StartNew();

        try
        {
            CleanDirectory(cliArgs.OutputDirectory);
            CleanDirectory(cliArgs.BinDirectory);
        }
        catch(IOException ex)
        {
            Console.Error.WriteLine($"error: Failed to clean build directories: {ex.Message}");
            return 1;
        }

        cleanStopwatch.Stop();
        reporter.ReportStageComplete(0, cleanStopwatch.Elapsed, new[]
        {
            $"Cleaned {cliArgs.OutputDirectory}",
            $"Cleaned {cliArgs.BinDirectory}",
        });

        return cliArgs.Mode switch
        {
            CompilerMode.Lex => RunLexMode(sourceFiles, cliArgs.OutputDirectory),
            _ => RunCompileMode(sourceFiles, cliArgs, reporter, totalStopwatch),
        };
    }

    private static int RunLexMode(List<string> sourceFiles, string outputDirectory)
    {
        var lexer = new MistLexer();
        var lexOutDir = Path.Combine(outputDirectory, "lexer");
        Directory.CreateDirectory(lexOutDir);

        var hasErrors = false;

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

            var sourceText = new Mist.Common.Text.SourceText(filePath, source);
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
                    continue;

                if(token.Channel == Storm.Runtime.TokenChannel.Hidden)
                {
                    sb.Append(token.Lexeme);
                }
                else
                {
                    var kindName = ((Mist.Generated.TokenKind)token.Kind).ToString();
                    sb.Append($"[{kindName}: {token.Lexeme}]");
                }
            }

            var outFileName = Path.GetFileNameWithoutExtension(filePath) + ".tokens";
            var outPath = Path.Combine(lexOutDir, outFileName);
            File.WriteAllText(outPath, sb.ToString());

            // Reconstruct source from token lexemes and write .mist file
            var reconstructed = string.Concat(
                tokenStream.AllTokens
                    .Where(t => t.Kind != (int)Mist.Generated.TokenKind.EndOfFile)
                    .Select(t => t.Lexeme));

            var reconstructedPath = Path.Combine(lexOutDir, Path.GetFileName(filePath));
            File.WriteAllText(reconstructedPath, reconstructed);

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

            Console.WriteLine($"Lexed {filePath} -> {outPath}, {reconstructedPath}");
        }

        return hasErrors ? 1 : 0;
    }

    private static int RunCompileMode(List<string> sourceFiles, CliArguments cliArgs, ICompilationProgressReporter reporter, Stopwatch totalStopwatch)
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

    private static CompilerResult CompileProject(List<string> sourceFiles, CliArguments cliArgs, ICompilationProgressReporter? progressReporter = null)
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
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EntryPoint: config.EntryPoint);

        // Construct the compiler pipeline
        var lexer = new MistLexer();
        var parser = new MistParser();
        var semanticModelBuilder = new SemanticModelBuilder();
        ImmutableArray<IAnalyzer> analyzers = [new TypeCheckerAnalyzer(), new ContextCheckerAnalyzer(), new ReadonlyAnalyzer(), new OwnershipAnalyzer(), new DropAnalyzer(), new EntryPointAnalyzer()];
        ImmutableArray<IBackend> backends = [new CSharpTranspilerBackend()];

        var driver = new CompilerDriver(lexer, parser, semanticModelBuilder, analyzers, backends, progressReporter);

        // Run compilation
        return driver.Compile(options);
    }

    private static int RunRunCommand(CliArguments cliArgs)
    {
        ICompilationProgressReporter reporter = cliArgs.Quiet
            ? NullProgressReporter.Instance
            : new ConsoleProgressReporter(Console.Out);
        var totalStopwatch = Stopwatch.StartNew();

        // Discover all .mist source files from the project folder, sorted ordinally.
        var sourceFiles = Directory.EnumerateFiles(cliArgs.ProjectFolder, "*.mist", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if(sourceFiles.Count == 0)
        {
            Console.Error.WriteLine("error: No .mist source files found.");
            return 1;
        }

        // Stage 0: Cleaning
        reporter.ReportStageStart(0, "Cleaning");
        var cleanStopwatch = Stopwatch.StartNew();

        try
        {
            CleanDirectory(cliArgs.OutputDirectory);
            CleanDirectory(cliArgs.BinDirectory);
        }
        catch(IOException ex)
        {
            Console.Error.WriteLine($"error: Failed to clean build directories: {ex.Message}");
            return 1;
        }

        cleanStopwatch.Stop();
        reporter.ReportStageComplete(0, cleanStopwatch.Elapsed, new[]
        {
            $"Cleaned {cliArgs.OutputDirectory}",
            $"Cleaned {cliArgs.BinDirectory}",
        });

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
        if(!Directory.Exists(path)) return;
        var di = new DirectoryInfo(path);
        foreach(var file in di.GetFiles()) file.Delete();
        foreach(var dir in di.GetDirectories()) dir.Delete(true);
    }

    private static string Escape(char c) => c switch
    {
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        ' ' => "\\s",
        _ => c.ToString(),
    };
}