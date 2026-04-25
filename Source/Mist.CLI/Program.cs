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
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

// Parse CLI arguments
var parseResult = CliArgumentParser.Parse(args);

if (parseResult.Errors.Count > 0)
{
    foreach (var error in parseResult.Errors)
    {
        Console.Error.WriteLine($"error: {error.Message}");
    }

    Console.Error.WriteLine("Usage: mist [source-files...] [--backend <id>] [--output <dir>]");
    return 1;
}

var cliArgs = parseResult.Arguments!;

// Discover .mist files recursively if no source files provided
var sourceFiles = cliArgs.SourceFiles.ToList();
if (sourceFiles.Count == 0)
{
    sourceFiles.AddRange(DiscoverMistFiles(Directory.GetCurrentDirectory()));
}

if (sourceFiles.Count == 0)
{
    Console.Error.WriteLine("error: No .mist source files found.");
    return 1;
}

// Map CLI arguments to CompilerOptions
var options = new CompilerOptions(
    SourcePaths: [.. sourceFiles],
    Backend: new BackendId(cliArgs.BackendId),
    OutputDirectory: cliArgs.OutputDirectory,
    EnabledAnalyzerIds: ImmutableArray<string>.Empty);

// Construct the compiler pipeline
ILexer lexer = new MistLexer();
IParser parser = new MistParser();
ISemanticModelBuilder semanticModelBuilder = new SemanticModelBuilder();
ImmutableArray<IAnalyzer> analyzers = [new TypeCheckerAnalyzer()];
ImmutableArray<IBackend> backends = [new CSharpTranspilerBackend()];

var driver = new CompilerDriver(lexer, parser, semanticModelBuilder, analyzers, backends);

// Run compilation
var result = driver.Compile(options);

// Format diagnostics to stderr
foreach (var diagnostic in result.Diagnostics)
{
    Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
}

// Exit code
return result.Succeeded ? 0 : 1;

static IEnumerable<string> DiscoverMistFiles(string directory)
{
    return Directory.EnumerateFiles(directory, "*.mist", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.Ordinal);
}
