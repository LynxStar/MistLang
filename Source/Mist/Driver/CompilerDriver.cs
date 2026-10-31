using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
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
    private readonly StdlibParseCache stdlibParseCache;
    private readonly ICompilationProgressReporter progressReporter;
    private readonly IResolutionDiagnosticListener? resolutionListener;
    private readonly ICompilationPipeline standardPipeline;
    private readonly BackendRegistry backendRegistry;
    private readonly Func<PipelineInput, IAmbientTypeSource> ambientTypes;

    public CompilerDriver(
        ILexer lexer,
        IParser parser,
        ICompilationProgressReporter? progressReporter = null,
        IResolutionDiagnosticListener? resolutionListener = null,
        Func<PipelineInput, IAmbientTypeSource>? ambientTypes = null,
        BackendRegistry? backendRegistry = null)
        : this(lexer, parser, StdlibParseCache.Shared, progressReporter, resolutionListener, ambientTypes, backendRegistry)
    {
    }

    internal CompilerDriver(
        ILexer lexer,
        IParser parser,
        StdlibParseCache stdlibParseCache,
        ICompilationProgressReporter? progressReporter = null,
        IResolutionDiagnosticListener? resolutionListener = null,
        Func<PipelineInput, IAmbientTypeSource>? ambientTypes = null,
        BackendRegistry? backendRegistry = null,
        ICompilationPipeline? pipeline = null)
    {
        this.lexer = lexer;
        this.parser = parser;
        this.stdlibParseCache = stdlibParseCache;
        this.progressReporter = progressReporter ?? NullProgressReporter.Instance;
        this.resolutionListener = resolutionListener;
        standardPipeline = pipeline ?? new StandardPipeline(this.progressReporter);
        this.ambientTypes = ambientTypes ?? StandardPipeline.LoadAmbientTypes;
        this.backendRegistry = backendRegistry ?? new BackendRegistry();
    }

    public CompilerResult Compile(CompilerOptions options)
    {
        var pipeline = options.Pipeline switch
        {
            PipelineMode.Standard => standardPipeline,
            _ => throw new InvalidOperationException($"Unknown pipeline: {options.Pipeline}."),
        };

        var operation = options.DiagnosticOperation ?? new DiagnosticOperation();
        var allDiagnostics = new DiagnosticBag(operation);

        var backend = backendRegistry.Resolve(options.Backend);
        if(backend is null)
        {
            allDiagnostics.ReportTyped(DriverDiagnostics.UnknownBackend, new Absent(AbsenceReason.Unavailable), new NameValue(options.Backend.Value));

            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        if(!string.IsNullOrEmpty(options.ProjectFolder))
        {
            var manifestPath = Path.GetFullPath(Path.Combine(options.ProjectFolder, ProjectFileParser.FileName));
            var chain = BuildChain.Current();
            if(BuildChain.Contains(chain, manifestPath))
            {
                allDiagnostics.ReportTyped(DriverDiagnostics.ReferenceCycle, new FileOnly(manifestPath),
                    new SequenceValue(SlotKind.Name, [.. BuildChain.Extend(chain, manifestPath).Select(path => new NameValue(path))], " -> "),
                    new OptionalValue(SlotKind.Path, null), new OptionalValue(SlotKind.Name, null));
                return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
            }
        }

        var runtimeIdentifier = TargetRuntime.Select(
            options.Platform, RuntimeInformation.RuntimeIdentifier, RuntimeInformation.OSArchitecture, options.RuntimeIdentifier);

        // Stage 1: Loading
        var loadResult = LoadCompilationUnits(options, backend, runtimeIdentifier, allDiagnostics);
        if(loadResult is null)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        var compilation = loadResult.Compilation;
        options = ProjectDependencyUnion.Apply(options, loadResult.ReferencedConfigurations);
        var servesFrameworkAssets = StaticAssetDeployment.ServesFrameworkAssets(
            options.OutputType,
            options.FrameworkReferences,
            () => StaticAssetDeployment.HasComponentFiles(options.ProjectFolder, options.OutputDirectory, options.BinDirectory));
        options = options with
        {
            Packages = StaticAssetDeployment.WithFrameworkAssetsPack(
                options.Packages,
                servesFrameworkAssets,
                () => FrameworkReferenceResolver.ResolvedVersion(StaticAssetDeployment.AspNetCoreFramework, options.TargetFramework)),
        };

        // Stage 1.5: Package Resolution (runs only when deps are declared)
        var resolutionDiagnostics = new ResolutionDiagnostics(allDiagnostics.ToImmutableArray().Length, resolutionListener, operation);
        var (packagePaths, packages) = ResolvePackages(options, resolutionDiagnostics, runtimeIdentifier);
        var resolvedAssemblyPaths = WithExternalAssemblies(packagePaths.AddRange(options.AssemblyPaths), loadResult);

        var ridGraph = packages.RuntimeIdentifierGraph ?? RidGraph.BuiltIn;
        if(!RidGraph.Contains(ridGraph, runtimeIdentifier))
        {
            allDiagnostics.ReportTyped(DriverDiagnostics.RuntimeIdentifierUnknown, new Absent(AbsenceReason.Unavailable), new NameValue(runtimeIdentifier));
            return resolutionDiagnostics.JoinInto(new CompilerResult(false, null, allDiagnostics.ToImmutableArray()));
        }

        var pipelineInput = new PipelineInput(
            compilation, resolvedAssemblyPaths,
            options, allDiagnostics, resolutionDiagnostics.Reported,
            loadResult.ExternalAssemblies, loadResult.ComposedProjects,
            RidGraph.FallbackOrder(ridGraph, runtimeIdentifier), packages.RuntimeAssets,
            StaticAssetDeployment.FrameworkAssetsDirectory(servesFrameworkAssets, packages));

        using var ambient = ambientTypes(pipelineInput);
        pipelineInput = pipelineInput with { AmbientTypes = ambient };

        // Stage 2: Lexing
        var lexResult = Lex(compilation, allDiagnostics);

        // Stage 3: Parsing
        compilation = Parse(
            compilation,
            lexResult.TokenStreams,
            lexResult.StdlibTokensForCache,
            lexResult.CachedStdlib,
            allDiagnostics);

        if(!backend.SourceTranslators.IsEmpty)
        {
            compilation = new TranslationStage(lexer, parser, progressReporter).Run(
                backend, loadResult.ClaimedInputs, compilation, pipelineInput, ambient);
        }

        pipelineInput = pipelineInput with { Compilation = compilation };

        return resolutionDiagnostics.JoinInto(pipeline.Execute(pipelineInput));
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

            var lexBag = new DiagnosticBag(allDiagnostics.Operation);
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
    /// Stage 3 – Parsing: parses each unit's token stream, collects diagnostics,
    /// and writes the stdlib cache if this is the first successful compile.
    /// </summary>
    internal MistCompilation Parse(
        MistCompilation compilation,
        Dictionary<CompilationUnit, TokenStream> tokenStreams,
        ImmutableArray<TokenStream>.Builder? stdlibTokensForCache,
        StdlibParseCache.Entry? cachedStdlib,
        DiagnosticBag allDiagnostics)
    {
        var parsingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("3", "Parsing");

        var units = ImmutableArray.CreateBuilder<CompilationUnit>(compilation.Units.Length);
        var stdlibAstsForCache = cachedStdlib is not null
            ? null
            : ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();

        var cachedIndex = 0;

        foreach(var unit in compilation.Units)
        {
            if(unit.IsStdlib && cachedStdlib is not null)
            {
                units.Add(unit with { Ast = cachedStdlib.Asts[cachedIndex++] });
                continue;
            }

            if(!tokenStreams.TryGetValue(unit, out var tokenStream))
            {
                units.Add(unit);
                continue;
            }

            var parseBag = new DiagnosticBag(allDiagnostics.Operation);
            var unitParseResult = parser.Parse(tokenStream, parseBag);

            foreach(var d in parseBag.ToImmutableArray())
            {
                allDiagnostics.Add(d);
            }

            foreach(var d in unitParseResult.Diagnostics)
            {
                allDiagnostics.Add(d);
            }

            if(unitParseResult.Root is not null && !parseBag.HasErrors
                && !unitParseResult.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                unitParseResult.Root.SetParentLinks();
                units.Add(unit with { Ast = unitParseResult.Root });
                if(unit.IsStdlib)
                {
                    stdlibAstsForCache?.Add(unitParseResult.Root);
                }
            }
            else
            {
                units.Add(unit);
            }
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
        var totalNodes = units.Sum(unit => unit.Ast is { } ast ? SyntaxTreeMetrics.CountNodes(ast) : 0);
        progressReporter.ReportStageComplete("3", parsingStopwatch.Elapsed, new[]
        {
            $"{totalNodes} AST nodes",
        });

        return compilation with { Units = units.MoveToImmutable() };
    }

    /// <summary>
    /// Stage 1.5 – Package Resolution: resolves NuGet packages and framework
    /// references, returning the merged set of assembly paths. Returns an empty
    /// array when no packages or framework references are declared.
    /// On resolution failure: continues with whatever resolved successfully.
    /// </summary>
    private (ImmutableArray<string> AssemblyPaths, PackageResolutionResult Packages) ResolvePackages(
        CompilerOptions options, ResolutionDiagnostics resolutionDiagnostics, string runtimeIdentifier)
    {
        var hasPackages = !options.Packages.IsDefaultOrEmpty;
        var hasFrameworkRefs = !options.FrameworkReferences.IsDefaultOrEmpty;

        if(!hasPackages && !hasFrameworkRefs)
        {
            return ([], new PackageResolutionResult([], [], FailureCount: 0));
        }

        var resolutionStopwatch = Stopwatch.StartNew();

        if(options.VerboseResolution)
        {
            progressReporter.ReportStageStart("1.5", "Package Resolution");
        }

        // Resolve NuGet packages (passes framework references for deduplication
        // against framework-provided assemblies to prevent duplicate loading)
        var packageResult = PackageResolver.ResolvePackages(
            options.Packages, options.FrameworkReferences, options.TargetFramework, resolutionDiagnostics.Reported,
            resolutionDiagnostics.Deliver, runtimeIdentifier);

        // Resolve framework references
        var frameworkResult = FrameworkReferenceResolver.Resolve(
            options.FrameworkReferences, options.TargetFramework, resolutionDiagnostics.Reported);
        resolutionDiagnostics.Deliver();

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
        return (resolvedAssemblyPaths, packageResult);
    }
}