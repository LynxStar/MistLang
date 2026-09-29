using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Driver;

/// <summary>
/// Orchestrates Stages 1-3 (loading, lexing, parsing), then dispatches to
/// the selected <see cref="ICompilationPipeline"/> for Stages 4-6.
/// </summary>
public sealed partial class CompilerDriver : ICompilerDriver
{
    private readonly ILexer lexer;
    private readonly IParser parser;
    private readonly ImmutableHashSet<string> backendIds;
    private readonly StdlibParseCache stdlibParseCache;
    private readonly ICompilationProgressReporter progressReporter;
    private readonly ICompilationPipeline? classicPipeline;
    private readonly ICompilationPipeline experimentalPipeline;

    public CompilerDriver(ILexer lexer, IParser parser, ICompilationProgressReporter? progressReporter = null)
        : this(lexer, parser, StdlibParseCache.Shared, progressReporter)
    {
    }

    internal CompilerDriver(
        ILexer lexer,
        IParser parser,
        StdlibParseCache stdlibParseCache,
        ICompilationProgressReporter? progressReporter = null)
        : this(lexer, parser, stdlibParseCache, progressReporter, [BackendId.CSharp], classicPipeline: null)
    {
    }

    private CompilerDriver(
        ILexer lexer,
        IParser parser,
        StdlibParseCache stdlibParseCache,
        ICompilationProgressReporter? progressReporter,
        IEnumerable<BackendId> backendIds,
        ICompilationPipeline? classicPipeline)
    {
        this.lexer = lexer;
        this.parser = parser;
        this.backendIds = backendIds.Select(id => id.Value).ToImmutableHashSet(StringComparer.Ordinal);
        this.stdlibParseCache = stdlibParseCache;
        this.progressReporter = progressReporter ?? NullProgressReporter.Instance;
        this.classicPipeline = classicPipeline;
        experimentalPipeline = new ExperimentalPipeline(this.progressReporter);
    }

    public CompilerResult Compile(CompilerOptions options)
    {
        var pipeline = options.Pipeline == PipelineMode.Classic
            ? classicPipeline ?? throw new InvalidOperationException(
                "This CompilerDriver was constructed without the classic pipeline; construct it with the classic services or select PipelineMode.Experimental.")
            : experimentalPipeline
            ;

        var allDiagnostics = new DiagnosticBag();

        if(!backendIds.Contains(options.Backend.Value))
        {
            allDiagnostics.Report(DriverDiagnostics.UnknownBackend, default, "<unknown>", (object?)options.Backend.Value);

            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Stage 1: Loading
        var loadResult = LoadCompilationUnits(options, allDiagnostics);
        if(loadResult is null)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        var compilation = loadResult.Compilation;
        var directReferencePaths = loadResult.DirectReferencePaths;
        options = ProjectDependencyUnion.Apply(options, loadResult.ReferencedConfigurations);

        // Stage 1.5: Package Resolution (runs only when deps are declared)
        var resolvedAssemblyPaths = ResolvePackages(options, allDiagnostics).AddRange(options.AssemblyPaths);

        // Stage 2: Lexing
        var lexResult = Lex(compilation, allDiagnostics);

        // Stage 3: Parsing
        var parseResult = Parse(
            compilation,
            lexResult.TokenStreams,
            lexResult.StdlibTokensForCache,
            lexResult.CachedStdlib,
            allDiagnostics);

        // Dispatch to the compilation pipeline (Stages 4-6).
        var pipelineInput = new PipelineInput(
            parseResult.Compilation, parseResult.Asts, resolvedAssemblyPaths,
            options, directReferencePaths, allDiagnostics);

        return pipeline.Execute(pipelineInput);
    }

    /// <summary>
    /// Result of Stage 1 (Loading): the combined compilation and any
    /// directly-referenced project paths for transitive visibility enforcement.
    /// </summary>
    private sealed record LoadResult(
        MistCompilation Compilation,
        ImmutableHashSet<string>? DirectReferencePaths,
        ImmutableArray<ProjectConfiguration> ReferencedConfigurations);

    /// <summary>
    /// Stage 1 – Loading: reads user source files, stdlib, and project
    /// references, combining them into a single <see cref="MistCompilation"/>.
    /// Returns <c>null</c> when a fatal error prevents compilation from proceeding.
    /// </summary>
    private LoadResult? LoadCompilationUnits(
        CompilerOptions options,
        DiagnosticBag allDiagnostics)
    {
        var loadingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("1", "Loading");

        // Step 1a: Load source files into CompilationUnits
        var userUnits = ImmutableArray.CreateBuilder<CompilationUnit>();
        foreach(var path in options.SourcePaths)
        {
            try
            {
                var contents = File.ReadAllText(path);
                var sourceText = new SourceText(path, contents);
                userUnits.Add(new CompilationUnit(sourceText));
            }
            catch(Exception ex)
            {
                allDiagnostics.Report(DriverDiagnostics.FileError, default, path, (object?)path, ex.Message);
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
        var stdlibUnits = StdlibLoader.LoadFor(stdlibBag);

        foreach(var d in stdlibBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(stdlibBag.HasErrors)
        {
            return null;
        }

        // Step 1c: Resolve project references (if any)
        // Only attempt reference resolution when a mist.toml exists in the
        // project folder. Many test scenarios and CLI invocations supply a
        // ProjectFolder without a mist.toml; in those cases we skip this
        // step entirely.
        var refUnits = ImmutableArray<CompilationUnit>.Empty;
        var refProjects = ImmutableArray<MistProject>.Empty;
        var refConfigurations = ImmutableArray<ProjectConfiguration>.Empty;
        ImmutableHashSet<string>? directReferencePaths = null;

        // Track the root project's global usings. When a mist.toml is parsed,
        // we use its GlobalUsings for user units; otherwise we fall back to
        // CompilerOptions.GlobalUsings (the test path).
        var userGlobalUsings = options.GlobalUsings.IsDefault
            ? ImmutableArray<string>.Empty
            : options.GlobalUsings;

        if(!string.IsNullOrEmpty(options.ProjectFolder)
            && File.Exists(Path.Combine(options.ProjectFolder, ProjectFileParser.FileName)))
        {
            var refParseBag = new DiagnosticBag();
            var projectConfig = ProjectFileParser.Parse(options.ProjectFolder, refParseBag);

            foreach(var d in refParseBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            if(projectConfig is not null && !refParseBag.HasErrors)
            {
                // Use the root project config's GlobalUsings for user units.
                userGlobalUsings = projectConfig.GlobalUsings.IsDefault
                    ? ImmutableArray<string>.Empty
                    : projectConfig.GlobalUsings;

                if(!projectConfig.References.IsEmpty)
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

                    foreach(var d in resolveBag.ToImmutableArray())
                    {
                        allDiagnostics.Add(d);
                    }

                    if(!resolveBag.HasErrors)
                    {
                        var refUnitBuilder = ImmutableArray.CreateBuilder<CompilationUnit>();
                        var refProjectBuilder = ImmutableArray.CreateBuilder<MistProject>(resolved.Length);
                        var refConfigurationBuilder = ImmutableArray.CreateBuilder<ProjectConfiguration>(resolved.Length);
                        foreach(var proj in resolved)
                        {
                            refConfigurationBuilder.Add(proj.Configuration);

                            // Record the project itself alongside its files, so later
                            // stages can group units by project without inferring the
                            // grouping back out of per-unit fields.
                            var projGlobalUsings = proj.Configuration.GlobalUsings.IsDefault
                                ? ImmutableArray<string>.Empty
                                : proj.Configuration.GlobalUsings;

                            refProjectBuilder.Add(new MistProject(
                                proj.Configuration.NamespaceRoot,
                                proj.ProjectFolder,
                                proj.CanonicalTomlPath)
                            {
                                GlobalUsings = projGlobalUsings,
                            });

                            foreach(var path in proj.SourcePaths)
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
                                catch(Exception ex)
                                {
                                    allDiagnostics.Report(DriverDiagnostics.FileError, default, path, (object?)path, ex.Message);
                                }
                            }
                        }

                        refUnits = refUnitBuilder.ToImmutable();
                        refProjects = refProjectBuilder.ToImmutable();
                        refConfigurations = refConfigurationBuilder.ToImmutable();
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            else if(refParseBag.HasErrors)
            {
                return null;
            }
        }

        // Step 1d: Combine units
        // Order: stdlib → referenced project units (deepest first) → user units
        var combinedUnits = ImmutableArray.CreateBuilder<CompilationUnit>(
            stdlibUnits.Length + refUnits.Length + userUnits.Count);
        combinedUnits.AddRange(stdlibUnits);
        combinedUnits.AddRange(refUnits);
        combinedUnits.AddRange(userUnits);

        var compilation = new MistCompilation(combinedUnits.ToImmutable(), options.EntryPoint, options.OutputType)
        {
            Projects = refProjects,
            HasStandardLibrary = !stdlibUnits.IsEmpty,
            GlobalUsings = userGlobalUsings,
        };

        loadingStopwatch.Stop();
        var stdlibCount = compilation.Units.Count(u => u.IsStdlib);
        var refCount = compilation.Units.Count(u => !u.IsStdlib && u.OriginProjectPath is not null);
        var userFileCount = compilation.Units.Count(u => !u.IsStdlib && u.OriginProjectPath is null);
        progressReporter.ReportStageComplete("1", loadingStopwatch.Elapsed, new[]
        {
            $"{stdlibCount} stdlib files",
            $"{refCount} referenced project files",
            $"{userFileCount} user files",
        });

        return new LoadResult(compilation, directReferencePaths, refConfigurations);
    }

    /// <summary>
    /// Result of Stage 2 (Lexing): the token streams for each successfully
    /// lexed unit, any stdlib tokens awaiting cache write, and the cached
    /// stdlib tuple (if present) for Stage 3 to consume.
    /// </summary>
    private sealed record LexResult(
        Dictionary<CompilationUnit, TokenStream> TokenStreams,
        ImmutableArray<TokenStream>.Builder? StdlibTokensForCache,
        StdlibParseCache.Entry? CachedStdlib);

    /// <summary>
    /// Stage 2 – Lexing: tokenizes each compilation unit, skipping stdlib
    /// units when the cache is populated. Reports progress and returns the
    /// token streams plus cache metadata for Stage 3.
    /// </summary>
    private LexResult Lex(MistCompilation compilation, DiagnosticBag allDiagnostics)
    {
        var lexingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("2", "Lexing");

        // Try to retrieve cached stdlib lex/parse results.
        var cachedStdlib = stdlibParseCache.TryGet(compilation.Units.Count(u => u.IsStdlib));

        var tokenStreams = new Dictionary<CompilationUnit, TokenStream>();
        var stdlibTokensForCache = cachedStdlib is not null
            ? null
            : ImmutableArray.CreateBuilder<TokenStream>();

        foreach(var unit in compilation.Units)
        {
            if(unit.IsStdlib && cachedStdlib is not null)
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
                if(unit.IsStdlib)
                {
                    stdlibTokensForCache?.Add(tokenStream);
                }
            }
            // If lex errors, skip parse for this unit (R16.3)
        }

        lexingStopwatch.Stop();
        var totalTokens = tokenStreams.Values.Sum(ts => ts.DefaultChannelTokens.Length);
        if(cachedStdlib is not null)
        {
            totalTokens += cachedStdlib.Tokens.Sum(ts => ts.DefaultChannelTokens.Length);
        }

        progressReporter.ReportStageComplete("2", lexingStopwatch.Elapsed, new[]
        {
            $"{totalTokens} tokens",
        });

        return new LexResult(tokenStreams, stdlibTokensForCache, cachedStdlib);
    }

    /// <summary>
    /// Result of Stage 3 (Parsing): the parsed ASTs and the (possibly updated)
    /// compilation reflecting any units that contain top-level statements.
    /// </summary>
    private sealed record ParseResult(
        ImmutableArray<Storm.Runtime.SyntaxNode> Asts,
        MistCompilation Compilation);

    /// <summary>
    /// Stage 3 – Parsing: parses each unit's token stream, collects diagnostics,
    /// detects top-level statements, and writes the stdlib cache if this is the
    /// first successful compile. Returns the ASTs and updated compilation.
    /// </summary>
    private ParseResult Parse(
        MistCompilation compilation,
        Dictionary<CompilationUnit, TokenStream> tokenStreams,
        ImmutableArray<TokenStream>.Builder? stdlibTokensForCache,
        StdlibParseCache.Entry? cachedStdlib,
        DiagnosticBag allDiagnostics)
    {
        var parsingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("3", "Parsing");

        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();
        var stdlibAstsForCache = cachedStdlib is not null
            ? null
            : ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();

        // If we have cached stdlib ASTs, prepend them first.
        if(cachedStdlib is not null)
        {
            asts.AddRange(cachedStdlib.Asts);
        }

        var unitsWithTopLevelStatements = new HashSet<CompilationUnit>();

        foreach(var (unit, tokenStream) in tokenStreams)
        {
            var parseBag = new DiagnosticBag();
            var unitParseResult = parser.Parse(tokenStream, parseBag);

            foreach(var d in parseBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            foreach(var d in unitParseResult.Diagnostics)
            {
                allDiagnostics.Add(d);
            }

            if(unitParseResult.Root is not null && !parseBag.HasErrors)
            {
                unitParseResult.Root.SetParentLinks();
                asts.Add(unitParseResult.Root);
                if(unit.IsStdlib)
                {
                    stdlibAstsForCache?.Add(unitParseResult.Root);
                }

                // Detect top-level statements in the parsed AST.
                if(unitParseResult.Root is CompilationUnitNode cu
                    && cu.Items.Any(item => item is StatementItemTopLevelItemNode))
                {
                    _ = unitsWithTopLevelStatements.Add(unit);
                }
            }
            // If parse errors, exclude AST from semantic model (R16.4)
        }

        // Update CompilationUnits that contain top-level statements.
        if(unitsWithTopLevelStatements.Count > 0)
        {
            var updatedUnits = compilation.Units.Select(u =>
                unitsWithTopLevelStatements.Contains(u)
                    ? u with { HasTopLevelStatements = true }
                    : u).ToImmutableArray();
            compilation = compilation with { Units = updatedUnits };
        }

        // Populate the stdlib cache if this is the first successful compile.
        if(stdlibTokensForCache is not null && stdlibAstsForCache is not null)
        {
            stdlibParseCache.Offer(
                compilation.Units.Count(u => u.IsStdlib),
                stdlibTokensForCache.ToImmutable(),
                stdlibAstsForCache.ToImmutable());
        }

        parsingStopwatch.Stop();
        var totalNodes = asts.Sum(SyntaxTreeMetrics.CountNodes);
        progressReporter.ReportStageComplete("3", parsingStopwatch.Elapsed, new[]
        {
            $"{totalNodes} AST nodes",
        });

        return new ParseResult(asts.ToImmutable(), compilation);
    }

    /// <summary>
    /// Stage 1.5 – Package Resolution: resolves NuGet packages and framework
    /// references, returning the merged set of assembly paths. Returns an empty
    /// array when no packages or framework references are declared.
    /// On resolution failure: continues with whatever resolved successfully.
    /// </summary>
    private ImmutableArray<string> ResolvePackages(CompilerOptions options, DiagnosticBag allDiagnostics)
    {
        var hasPackages = !options.Packages.IsDefaultOrEmpty;
        var hasFrameworkRefs = !options.FrameworkReferences.IsDefaultOrEmpty;

        if(!hasPackages && !hasFrameworkRefs)
        {
            return ImmutableArray<string>.Empty;
        }

        var resolutionStopwatch = Stopwatch.StartNew();

        if(options.VerboseResolution)
        {
            progressReporter.ReportStageStart("1.5", "Package Resolution");
        }

        var resolutionDiagnostics = new DiagnosticBag();

        // Resolve NuGet packages (passes framework references for deduplication
        // against framework-provided assemblies to prevent duplicate loading)
        var packageResult = PackageResolver.ResolvePackages(
            options.Packages, options.FrameworkReferences, options.TargetFramework, resolutionDiagnostics);

        // Resolve framework references
        var frameworkResult = FrameworkReferenceResolver.Resolve(
            options.FrameworkReferences, options.TargetFramework, resolutionDiagnostics);

        // Merge assembly paths (union, dedupe by path)
        var mergedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in packageResult.AssemblyPaths)
        {
            _ = mergedPaths.Add(path);
        }

        foreach(var path in frameworkResult.AssemblyPaths)
        {
            _ = mergedPaths.Add(path);
        }

        var resolvedAssemblyPaths = ImmutableArray.CreateRange(mergedPaths);

        // Add diagnostics to the shared bag (they always surface through
        // normal diagnostic printing regardless of verbose).
        foreach(var d in resolutionDiagnostics.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        resolutionStopwatch.Stop();

        // Verbose output: emit detail lines and summary
        if(options.VerboseResolution)
        {
            var details = new List<string>();

            // Per-package detail lines
            foreach(var pkg in packageResult.Resolved)
            {
                details.Add($"Restored {pkg.Name} {pkg.Version} ({pkg.SelectedTfm})");
            }

            // Per-framework detail lines
            foreach(var fw in frameworkResult.Resolved)
            {
                details.Add($"Resolved {fw.Name} {fw.Version}");
            }

            // Zero-assemblies notes for packages with AssemblyCount == 0
            foreach(var pkg in packageResult.Resolved)
            {
                if(pkg.AssemblyCount == 0)
                {
                    details.Add($"{pkg.Name} contributed no assemblies for {options.TargetFramework}");
                }
            }

            // Elapsed ms
            details.Add($"{(int)resolutionStopwatch.Elapsed.TotalMilliseconds} ms");

            // Summary line with success/failure counts
            var successCount = packageResult.Resolved.Length + frameworkResult.Resolved.Length;
            var failureCountTotal = packageResult.FailureCount + frameworkResult.FailureCount;
            details.Add($"{successCount} succeeded, {failureCountTotal} failed");

            progressReporter.ReportStageComplete("1.5", resolutionStopwatch.Elapsed, details);
        }

        // On resolution failure: continue the build with whatever resolved
        // successfully (Req 2.6, 5.8). Do NOT abort.
        return resolvedAssemblyPaths;
    }
}