using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;

namespace Mist.Driver;

/// <summary>
/// Orchestrates the full compilation pipeline: Lex → Parse → Build semantic model
/// → Run analyzers → Emit via backend, with short-circuit logic at each stage.
/// </summary>
public sealed class CompilerDriver : ICompilerDriver
{
    // ── Stdlib lex/parse cache ────────────────────────────────────────────
    // The stdlib files never change during a process lifetime. Caching the
    // lexed token streams and parsed ASTs avoids re-lexing/re-parsing 19
    // files on every Compile() call — a significant win for property-based
    // tests that compile hundreds of programs in a single test run.
    private static (ImmutableArray<TokenStream> Tokens, ImmutableArray<Storm.Runtime.SyntaxNode> Asts)? _stdlibCache;
    private static readonly object _stdlibCacheLock = new();

    private ILexer lexer;
    private IParser parser;
    private ISemanticModelBuilder semanticModelBuilder;
    private ImmutableArray<IAnalyzer> analyzers;
    private ImmutableDictionary<string, IBackend> backends;
    private readonly ICompilationProgressReporter progressReporter;

    public CompilerDriver(
        ILexer lexer,
        IParser parser,
        ISemanticModelBuilder semanticModelBuilder,
        ImmutableArray<IAnalyzer> analyzers,
        ImmutableArray<IBackend> backends,
        ICompilationProgressReporter? progressReporter = null)
    {
        this.lexer = lexer;
        this.parser = parser;
        this.semanticModelBuilder = semanticModelBuilder;
        this.analyzers = analyzers;
        this.backends = backends.ToImmutableDictionary(b => b.Id.Value);
        this.progressReporter = progressReporter ?? NullProgressReporter.Instance;
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

        // Stage 1: Loading
        var loadingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart(1, "Loading");

        // Step 1: Load source files into CompilationUnits
        var userUnits = ImmutableArray.CreateBuilder<CompilationUnit>();
        foreach(var path in options.SourcePaths)
        {
            try
            {
                var contents = File.ReadAllText(path);
                var sourceText = new Common.Text.SourceText(path, contents);
                userUnits.Add(new CompilationUnit(sourceText));
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

        // Step 1b: Load Standard Library sources and prepend them to the
        // user units (design § 8). The loader reads every *.mist file under
        // AppContext.BaseDirectory/Standard and returns them as
        // CompilationUnits with IsStdlib=true. If the loader emits any
        // MIST_STDLIB_PARSE error, the compilation cannot proceed because
        // primitive keyword resolution (Stage 4) and operator overload
        // resolution depend on stdlib types being present.
        var stdlibBag = new DiagnosticBag();
        var stdlibUnits = StdlibLoader.LoadFor(backend, stdlibBag);

        foreach(var d in stdlibBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(stdlibBag.HasErrors)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Step 1c: Resolve project references (if any)
        // Only attempt reference resolution when a mist.toml exists in the
        // project folder. Many test scenarios and CLI invocations supply a
        // ProjectFolder without a mist.toml; in those cases we skip this
        // step entirely.
        var refUnits = ImmutableArray<CompilationUnit>.Empty;
        ImmutableHashSet<string>? directReferencePaths = null;

        if (!string.IsNullOrEmpty(options.ProjectFolder)
            && File.Exists(Path.Combine(options.ProjectFolder, ProjectFileParser.FileName)))
        {
            var refParseBag = new DiagnosticBag();
            var projectConfig = ProjectFileParser.Parse(options.ProjectFolder, refParseBag);

            foreach (var d in refParseBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            if (projectConfig is not null && !projectConfig.References.IsEmpty
                && !refParseBag.HasErrors)
            {
                // Build the set of directly-referenced canonical paths from
                // the root project's declared references. This set is passed
                // to the semantic model builder to enforce transitive
                // visibility: only symbols from direct references (and
                // stdlib / user code) are visible during name resolution.
                directReferencePaths = projectConfig.References
                    .Select(r => r.Path)
                    .ToImmutableHashSet(StringComparer.Ordinal);

                var resolveBag = new DiagnosticBag();
                var resolved = ProjectReferenceResolver.Resolve(
                    options.ProjectFolder,
                    projectConfig.References,
                    resolveBag);

                foreach (var d in resolveBag.ToImmutableArray())
                {
                    allDiagnostics.Add(d);
                }

                if (!resolveBag.HasErrors)
                {
                    var refUnitBuilder = ImmutableArray.CreateBuilder<CompilationUnit>();
                    foreach (var proj in resolved)
                    {
                        foreach (var path in proj.SourcePaths)
                        {
                            try
                            {
                                var contents = File.ReadAllText(path);
                                var sourceText = new SourceText(path, contents);
                                refUnitBuilder.Add(new CompilationUnit(sourceText)
                                {
                                    NamespaceRoot = proj.Configuration.NamespaceRoot,
                                    ProjectFolder = proj.ProjectFolder,
                                    OriginProjectPath = proj.CanonicalTomlPath,
                                });
                            }
                            catch (Exception ex)
                            {
                                allDiagnostics.Add(new Diagnostic(
                                    new DiagnosticId("MIST_FILE_ERROR"),
                                    DiagnosticSeverity.Error,
                                    PipelineStage.Driver,
                                    $"Failed to read referenced source file '{path}': {ex.Message}",
                                    Location: null,
                                    CompilationUnitPath: path,
                                    AnalyzerName: null));
                            }
                        }
                    }
                    refUnits = refUnitBuilder.ToImmutable();
                }
                else
                {
                    return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
                }
            }
            else if (refParseBag.HasErrors)
            {
                return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
            }
        }

        // Step 1d: Combine units
        // Order: stdlib → referenced project units (deepest first) → user units
        var combinedUnits = ImmutableArray.CreateBuilder<CompilationUnit>(
            stdlibUnits.Length + refUnits.Length + userUnits.Count);
        combinedUnits.AddRange(stdlibUnits);
        combinedUnits.AddRange(refUnits);
        combinedUnits.AddRange(userUnits);

        var compilation = new MistCompilation(combinedUnits.ToImmutable());

        loadingStopwatch.Stop();
        var stdlibCount = compilation.Units.Count(u => u.IsStdlib);
        var refCount = compilation.Units.Count(u => !u.IsStdlib && u.OriginProjectPath is not null);
        var userFileCount = compilation.Units.Count(u => !u.IsStdlib && u.OriginProjectPath is null);
        progressReporter.ReportStageComplete(1, loadingStopwatch.Elapsed, new[]
        {
            $"{stdlibCount} stdlib files",
            $"{refCount} referenced project files",
            $"{userFileCount} user files",
        });

        // Stage 2: Lexing
        var lexingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart(2, "Lexing");

        // Try to retrieve cached stdlib lex/parse results.
        var cachedStdlib = _stdlibCache;

        var tokenStreams = new Dictionary<CompilationUnit, TokenStream>();
        var stdlibTokensForCache = cachedStdlib.HasValue
            ? null
            : ImmutableArray.CreateBuilder<TokenStream>();

        foreach(var unit in compilation.Units)
        {
            if (unit.IsStdlib && cachedStdlib.HasValue)
            {
                // Stdlib tokens are already cached; skip lexing.
                continue;
            }

            var lexBag = new DiagnosticBag();
            var tokenStream = lexer.Lex(unit.Source, lexBag);

            foreach(var d in lexBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            if(!lexBag.HasErrors)
            {
                tokenStreams[unit] = tokenStream;
                if (unit.IsStdlib)
                    stdlibTokensForCache?.Add(tokenStream);
            }
            // If lex errors, skip parse for this unit (R16.3)
        }

        lexingStopwatch.Stop();
        var totalTokens = tokenStreams.Values.Sum(ts => ts.DefaultChannelTokens.Length);
        if (cachedStdlib.HasValue)
            totalTokens += cachedStdlib.Value.Tokens.Sum(ts => ts.DefaultChannelTokens.Length);
        progressReporter.ReportStageComplete(2, lexingStopwatch.Elapsed, new[]
        {
            $"{totalTokens} tokens",
        });

        // Stage 3: Parsing
        var parsingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart(3, "Parsing");

        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();
        var stdlibAstsForCache = cachedStdlib.HasValue
            ? null
            : ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();

        // If we have cached stdlib ASTs, prepend them first.
        if (cachedStdlib.HasValue)
        {
            asts.AddRange(cachedStdlib.Value.Asts);
        }

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
                if (unit.IsStdlib)
                    stdlibAstsForCache?.Add(parseResult.Root);
            }
            // If parse errors, exclude AST from semantic model (R16.4)
        }

        // Populate the stdlib cache if this is the first successful compile.
        if (!cachedStdlib.HasValue && stdlibTokensForCache is not null && stdlibAstsForCache is not null)
        {
            lock (_stdlibCacheLock)
            {
                if (!_stdlibCache.HasValue)
                {
                    _stdlibCache = (stdlibTokensForCache.ToImmutable(), stdlibAstsForCache.ToImmutable());
                }
            }
        }

        parsingStopwatch.Stop();
        var totalNodes = asts.Sum(root => SyntaxTreeMetrics.CountNodes(root));
        progressReporter.ReportStageComplete(3, parsingStopwatch.Elapsed, new[]
        {
            $"{totalNodes} AST nodes",
        });

        // Stage 4: Semantic Model
        var semanticStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart(4, "Semantic Model");

        var ambientTypeResolver = backend.GetAmbientTypeResolver();
        var semanticBag = new DiagnosticBag();
        ISemanticModel? semanticModel;
        try
        {
            semanticModel = semanticModelBuilder.Build(compilation, asts.ToImmutable(), ambientTypeResolver, options.NamespaceRoot, options.ProjectFolder, semanticBag, directReferencePaths);
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

        semanticStopwatch.Stop();
        var topLevelConstructs = semanticModel?.AllSymbols.Count(s =>
            s.Origin == SymbolOrigin.UserCode &&
            s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Trait) ?? 0;
        progressReporter.ReportStageComplete(4, semanticStopwatch.Elapsed, new[]
        {
            $"{topLevelConstructs} top-level constructs",
        });

        foreach(var d in semanticBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(semanticModel is null)
        {
            // Skip analyzers and backend if semantic model fails (R16.5)
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Short-circuit if the semantic model produced errors.
        // The builder always returns a non-null model (using ErrorType to
        // suppress cascading issues), but error diagnostics still mean the
        // program is invalid and we should not proceed to emission.
        if(semanticBag.HasErrors)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Stage 4b: Readonly Inference
        // Run the inference engine after the semantic model is built but before
        // analyzers. This classifies members as InferredReadonly or Mutable.
        ReadonlyInferenceEngine.RunInference(semanticModel);

        // Stage 5: Analysis
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart(5, "Analysis");

        var filteredAnalyzers = options.EnabledAnalyzerIds.IsEmpty
            ? analyzers
            : analyzers.Where(a => options.EnabledAnalyzerIds.Contains(a.AnalyzerId)).ToImmutableArray();

        var runner = new AnalyzerRunner(filteredAnalyzers);
        var analyzerResult = runner.RunAll(semanticModel);

        analysisStopwatch.Stop();
        var analyzerDetails = analyzerResult.Timings
            .Select(entry => $"{entry.AnalyzerId}  {entry.Elapsed.TotalSeconds:F2}s")
            .ToArray();
        progressReporter.ReportStageComplete(5, analysisStopwatch.Elapsed, analyzerDetails);

        foreach(var d in analyzerResult.Diagnostics)
        {
            allDiagnostics.Add(d);
        }

        if(analyzerResult.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            // Skip backend if analyzer errors (R13.6)
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Stage 6: Emit via backend
        var emitStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart(6, "Emit");

        var backendBag = new DiagnosticBag();
        var projectName = options.ProjectName;

        IReadOnlyDictionary<string, string>? emittedFiles = null;
        BackendResult backendResult;

        if (options.EmitToDisk)
        {
            backendResult = backend.Emit(semanticModel, options.OutputDirectory, options.BinDirectory, projectName, backendBag);
        }
        else
        {
            // In-memory emit: use CSharpEmitter.EmitToMemory directly to avoid disk I/O.
            var files = Backends.CSharpTranspiler.CSharpEmitter.EmitToMemory(semanticModel, projectName, backendBag);
            emittedFiles = files;
            backendResult = new BackendResult(
                Succeeded: !backendBag.HasErrors,
                PrimaryArtifactPath: null,
                Diagnostics: backendBag.ToImmutableArray(),
                EmittedFileCount: files.Count);
        }

        emitStopwatch.Stop();
        var emitDetails = new List<string> { $"{backendResult.EmittedFileCount} .cs files written" };
        if (backendResult.PrimaryArtifactPath is not null)
        {
            emitDetails.Add(backendResult.PrimaryArtifactPath);
        }
        progressReporter.ReportStageComplete(6, emitStopwatch.Elapsed, emitDetails);

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
            allDiagnostics.ToImmutableArray(),
            emittedFiles);
    }
}
