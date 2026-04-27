using System.Collections.Immutable;
using Mist.Analysis;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;

namespace Mist.Driver;

/// <summary>
/// Orchestrates the full compilation pipeline: Lex → Parse → Build semantic model
/// → Run analyzers → Emit via backend, with short-circuit logic at each stage.
/// </summary>
public sealed class CompilerDriver : ICompilerDriver
{
    private ILexer lexer;
    private IParser parser;
    private ISemanticModelBuilder semanticModelBuilder;
    private ImmutableArray<IAnalyzer> analyzers;
    private ImmutableDictionary<string, IBackend> backends;

    public CompilerDriver(
        ILexer lexer,
        IParser parser,
        ISemanticModelBuilder semanticModelBuilder,
        ImmutableArray<IAnalyzer> analyzers,
        ImmutableArray<IBackend> backends)
    {
        this.lexer = lexer;
        this.parser = parser;
        this.semanticModelBuilder = semanticModelBuilder;
        this.analyzers = analyzers;
        this.backends = backends.ToImmutableDictionary(b => b.Id.Value);
    }

    public CompilerResult Compile(CompilerOptions options)
    {
        var allDiagnostics = new DiagnosticBag();

        // Resolve backend
        if(!backends.TryGetValue(options.Backend.Value, out var backend))
        {
            allDiagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_UNKNOWN_BACKEND"),
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Unknown backend '{options.Backend.Value}'.",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));

            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Step 1: Load source files into CompilationUnits
        var units = ImmutableArray.CreateBuilder<CompilationUnit>();
        foreach(var path in options.SourcePaths)
        {
            try
            {
                var contents = File.ReadAllText(path);
                var sourceText = new Common.Text.SourceText(path, contents);
                units.Add(new CompilationUnit(sourceText));
            }
            catch(Exception ex)
            {
                allDiagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_FILE_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Failed to read source file '{path}': {ex.Message}",
                    Location: null,
                    CompilationUnitPath: path,
                    AnalyzerName: null));
            }
        }

        var compilation = new MistCompilation(units.ToImmutable());

        // Step 2: Lex each compilation unit
        var tokenStreams = new Dictionary<CompilationUnit, TokenStream>();
        foreach(var unit in compilation.Units)
        {
            var lexBag = new DiagnosticBag();
            var tokenStream = lexer.Lex(unit.Source, lexBag);

            foreach(var d in lexBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            if(!lexBag.HasErrors)
            {
                tokenStreams[unit] = tokenStream;
            }
            // If lex errors, skip parse for this unit (R16.3)
        }

        // Step 3: Parse each successfully-lexed unit
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();
        foreach(var (unit, tokenStream) in tokenStreams)
        {
            var parseBag = new DiagnosticBag();
            var parseResult = parser.Parse(tokenStream, parseBag);

            foreach(var d in parseBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            foreach(var d in parseResult.Diagnostics)
            {
                allDiagnostics.Add(d);
            }

            if(parseResult.Root is not null && !parseBag.HasErrors)
            {
                asts.Add(parseResult.Root);
            }
            // If parse errors, exclude AST from semantic model (R16.4)
        }

        // Step 4: Build semantic model
        var ambientTypeResolver = backend.GetAmbientTypeResolver();
        var semanticBag = new DiagnosticBag();
        ISemanticModel? semanticModel;
        try
        {
            semanticModel = semanticModelBuilder.Build(compilation, asts.ToImmutable(), ambientTypeResolver, semanticBag);
        }
        catch(Exception ex)
        {
            allDiagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_SEMANTIC_FAILURE"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"Semantic model construction failed: {ex.Message}",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));
            semanticModel = null;
        }

        foreach(var d in semanticBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(semanticModel is null)
        {
            // Skip analyzers and backend if semantic model fails (R16.5)
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Step 5: Run analyzers
        var filteredAnalyzers = options.EnabledAnalyzerIds.IsEmpty
            ? analyzers
            : analyzers.Where(a => options.EnabledAnalyzerIds.Contains(a.AnalyzerId)).ToImmutableArray();

        var runner = new AnalyzerRunner(filteredAnalyzers);
        var analyzerDiagnostics = runner.RunAll(semanticModel);

        foreach(var d in analyzerDiagnostics)
        {
            allDiagnostics.Add(d);
        }

        if(analyzerDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            // Skip backend if analyzer errors (R13.6)
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Step 6: Emit via backend
        var backendBag = new DiagnosticBag();
        var backendResult = backend.Emit(semanticModel, options.OutputDirectory, options.BinDirectory, backendBag);

        foreach(var d in backendBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        foreach(var d in backendResult.Diagnostics)
        {
            allDiagnostics.Add(d);
        }

        return new CompilerResult(
            backendResult.Succeeded,
            backendResult.PrimaryArtifactPath,
            allDiagnostics.ToImmutableArray());
    }
}
