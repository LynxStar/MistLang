using System.Collections.Immutable;
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
        // Parse CLI arguments
        var parseResult = CliArgumentParser.Parse(args);

        if(parseResult.Errors.Count > 0)
        {
            foreach(var error in parseResult.Errors)
            {
                Console.Error.WriteLine($"error: {error.Message}");
            }

            Console.Error.WriteLine("Usage: mist [source-files...] [--mode <compile|lex>] [--backend <id>] [--output <dir>]");
            return 1;
        }

        var cliArgs = parseResult.Arguments!;

        // Resolve source files: expand directories to .mist files recursively,
        // and discover from cwd if no arguments provided.
        var sourceFiles = new List<string>();
        if(cliArgs.SourceFiles.Count == 0)
        {
            sourceFiles.AddRange(discoverMistFiles(Directory.GetCurrentDirectory()));
        }
        else
        {
            foreach(var path in cliArgs.SourceFiles)
            {
                if(Directory.Exists(path))
                {
                    sourceFiles.AddRange(discoverMistFiles(path));
                }
                else
                {
                    sourceFiles.Add(path);
                }
            }
        }

        if(sourceFiles.Count == 0)
        {
            Console.Error.WriteLine("error: No .mist source files found.");
            return 1;
        }

        return cliArgs.Mode switch
        {
            CompilerMode.Lex => RunLexMode(sourceFiles, cliArgs.OutputDirectory),
            _ => RunCompileMode(sourceFiles, cliArgs),
        };

        static IEnumerable<string> discoverMistFiles(string directory)
        {
            return Directory.EnumerateFiles(directory, "*.mist", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal);
        }
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

    private static int RunCompileMode(List<string> sourceFiles, CliArguments cliArgs)
    {
        // Map CLI arguments to CompilerOptions
        var options = new CompilerOptions(
            SourcePaths: [.. sourceFiles],
            Backend: new BackendId(cliArgs.BackendId),
            OutputDirectory: cliArgs.OutputDirectory,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        // Construct the compiler pipeline
        var lexer = new MistLexer();
        var parser = new MistParser();
        var semanticModelBuilder = new SemanticModelBuilder();
        ImmutableArray<IAnalyzer> analyzers = [new TypeCheckerAnalyzer()];
        ImmutableArray<IBackend> backends = [new CSharpTranspilerBackend()];

        var driver = new CompilerDriver(lexer, parser, semanticModelBuilder, analyzers, backends);

        // Run compilation
        var result = driver.Compile(options);

        // Format diagnostics to stderr
        foreach(var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        // Exit code
        return result.Succeeded ? 0 : 1;
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