using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Driver;

/// <summary>
/// Orchestrates Stages 1-3 (loading, lexing, parsing), then dispatches to
/// the selected <see cref="ICompilationPipeline"/> for Stages 4-6.
/// </summary>
public sealed class CompilerDriver : ICompilerDriver
{
    private readonly ILexer lexer;
    private readonly IParser parser;
    private readonly StdlibParseCache stdlibParseCache;
    private readonly ICompilationProgressReporter progressReporter;
    private readonly IResolutionDiagnosticListener? resolutionListener;
    private readonly ICompilationPipeline standardPipeline;

    public CompilerDriver(
        ILexer lexer,
        IParser parser,
        ICompilationProgressReporter? progressReporter = null,
        IResolutionDiagnosticListener? resolutionListener = null,
        Func<PipelineInput, IAmbientTypeSource>? ambientTypes = null)
        : this(lexer, parser, StdlibParseCache.Shared, progressReporter, resolutionListener, ambientTypes)
    {
    }

    internal CompilerDriver(
        ILexer lexer,
        IParser parser,
        StdlibParseCache stdlibParseCache,
        ICompilationProgressReporter? progressReporter = null,
        IResolutionDiagnosticListener? resolutionListener = null,
        Func<PipelineInput, IAmbientTypeSource>? ambientTypes = null)
    {
        this.lexer = lexer;
        this.parser = parser;
        this.stdlibParseCache = stdlibParseCache;
        this.progressReporter = progressReporter ?? NullProgressReporter.Instance;
        this.resolutionListener = resolutionListener;
        standardPipeline = new StandardPipeline(this.progressReporter, ambientTypes);
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

        if(options.Backend != BackendId.CSharp)
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

        var loadResult = LoadCompilationUnits(options, runtimeIdentifier, allDiagnostics);
        if(loadResult is null)
        {
            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        var compilation = loadResult.Compilation;
        options = ProjectDependencyUnion.Apply(options, loadResult.ReferencedConfigurations);

        var resolutionDiagnostics = new ResolutionDiagnostics(allDiagnostics.ToImmutableArray().Length, resolutionListener, operation);
        var (packagePaths, packages) = ResolvePackages(options, resolutionDiagnostics, runtimeIdentifier);
        var resolvedAssemblyPaths = WithExternalAssemblies(packagePaths.AddRange(options.AssemblyPaths), loadResult);

        var ridGraph = packages.RuntimeIdentifierGraph ?? RidGraph.BuiltIn;
        if(!RidGraph.Contains(ridGraph, runtimeIdentifier))
        {
            allDiagnostics.ReportTyped(DriverDiagnostics.RuntimeIdentifierUnknown, new Absent(AbsenceReason.Unavailable), new NameValue(runtimeIdentifier));
            return resolutionDiagnostics.JoinInto(new CompilerResult(false, null, allDiagnostics.ToImmutableArray()));
        }

        var lexResult = Lex(compilation, allDiagnostics);

        compilation = Parse(
            compilation,
            lexResult.TokenStreams,
            lexResult.StdlibTokensForCache,
            lexResult.CachedStdlib,
            allDiagnostics);

        var pipelineInput = new PipelineInput(
            compilation, resolvedAssemblyPaths,
            options, allDiagnostics, resolutionDiagnostics.Reported,
            loadResult.ExternalAssemblies, loadResult.ComposedProjects,
            RidGraph.FallbackOrder(ridGraph, runtimeIdentifier), packages.RuntimeAssets);

        return resolutionDiagnostics.JoinInto(pipeline.Execute(pipelineInput));
    }

    private sealed record LoadResult(
        MistCompilation Compilation,
        ImmutableArray<ProjectConfiguration> ReferencedConfigurations,
        ImmutableArray<ExternalAssembly> ExternalAssemblies,
        ImmutableArray<ComposedProject> ComposedProjects,
        int BuiltCSharpProjectCount);

    private sealed record ManifestLoad(ImmutableArray<string> GlobalUsings, LoadedReferences References);

    private static ImmutableArray<string> WithExternalAssemblies(ImmutableArray<string> resolved, LoadResult loadResult)
    {
        var composedNames = loadResult.ComposedProjects.Select(project => project.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths = resolved.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return resolved.AddRange(loadResult.ExternalAssemblies
            .Select(external => external.Path)
            .Where(path => !composedNames.Contains(Path.GetFileNameWithoutExtension(path)) && paths.Add(path)));
    }

    private LoadResult? LoadCompilationUnits(
        CompilerOptions options,
        string runtimeIdentifier,
        DiagnosticBag allDiagnostics)
    {
        var loadingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("1", "Loading");

        var loadResult = ReadCompilationUnits(options, allDiagnostics);

        loadingStopwatch.Stop();
        progressReporter.ReportStageComplete(
            "1",
            loadingStopwatch.Elapsed,
            LoadingDetails(
                loadResult?.Compilation,
                loadResult?.BuiltCSharpProjectCount ?? 0,
                loadResult?.ExternalAssemblies.Length ?? 0,
                loadResult is not null && MayDeployRuntimeAssets(options, loadResult) ? runtimeIdentifier : null));

        return loadResult;
    }

    // Stage 1 completes before Stage 1.5 restores the packages, so it states the identifier whenever a package or an
    // external assembly could supply a runtime asset, which covers every build in which one is selected.
    private static bool MayDeployRuntimeAssets(CompilerOptions options, LoadResult loadResult)
    {
        return !options.Packages.IsDefaultOrEmpty
            || !loadResult.ExternalAssemblies.IsDefaultOrEmpty
            || loadResult.ReferencedConfigurations.Any(configuration => !configuration.Packages.IsDefaultOrEmpty);
    }

    internal static IReadOnlyList<string> LoadingDetails(
        MistCompilation? compilation, int builtCSharpProjects, int externalAssemblies, string? runtimeIdentifier = null)
    {
        if(compilation is null)
        {
            return [];
        }

        var stdlibCount = compilation.Units.Count(u => u.IsStdlib);
        var refCount = compilation.Units.Count(u => !u.IsStdlib && u.OriginProjectPath is not null);
        var userFileCount = compilation.Units.Count(u => !u.IsStdlib && u.OriginProjectPath is null);

        string[] files =
        [
            $"{stdlibCount} stdlib files",
            $"{refCount} referenced project files",
            $"{userFileCount} user files",
        ];

        var references = builtCSharpProjects == 0 && externalAssemblies == 0
            ? files
            : [.. files, $"{builtCSharpProjects} C# projects built", $"{externalAssemblies} external assemblies"];

        return runtimeIdentifier is null ? references : [.. references, $"runtime identifier {runtimeIdentifier}"];
    }

    private static LoadResult? ReadCompilationUnits(
        CompilerOptions options,
        DiagnosticBag allDiagnostics)
    {
        // Every referenced C# project is built before any source is read (csharp-and-assembly-references
        // Requirement 4.1).
        var manifest = ReadManifest(options, allDiagnostics);
        if(manifest is null)
        {
            return null;
        }

        var userUnits = ImmutableArray.CreateBuilder<CompilationUnit>();
        foreach(var path in options.SourcePaths)
        {
            try
            {
                var contents = File.ReadAllText(path);
                var sourceText = new SourceText(path, contents);
                userUnits.Add(new CompilationUnit(sourceText, options.NamespaceRoot, options.ProjectFolder));
            }
            catch(Exception ex)
            {
                allDiagnostics.ReportTyped(DriverDiagnostics.FileError, new FileOnly(path), new PathValue(path), new TextValue(ex.Message));
            }
        }

        // Stdlib units precede the user's (design § 8). A stdlib error stops the build because Stage 4 resolves
        // primitive keywords and operator overloads against stdlib types.
        var stdlibBag = new DiagnosticBag(allDiagnostics.Operation);
        var stdlibUnits = StdlibLoader.LoadFor(stdlibBag);

        foreach(var d in stdlibBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(stdlibBag.HasErrors)
        {
            return null;
        }

        var resolved = manifest.References.Projects;
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
                EntryPoint = proj.Configuration.EntryPoint,
                OutputType = proj.Configuration.OutputType,
            });

            foreach(var path in proj.SourcePaths)
            {
                try
                {
                    var contents = File.ReadAllText(path);
                    var sourceText = new SourceText(path, contents);
                    refUnitBuilder.Add(new CompilationUnit(sourceText, proj.Configuration.NamespaceRoot, proj.ProjectFolder)
                    {
                        OriginProjectPath = proj.CanonicalTomlPath,
                    });
                }
                catch(Exception ex)
                {
                    allDiagnostics.ReportTyped(DriverDiagnostics.FileError, new FileOnly(path), new PathValue(path), new TextValue(ex.Message));
                }
            }
        }

        // Referenced project units arrive deepest first.
        var combinedUnits = ImmutableArray.CreateBuilder<CompilationUnit>(
            stdlibUnits.Length + refUnitBuilder.Count + userUnits.Count);
        combinedUnits.AddRange(stdlibUnits);
        combinedUnits.AddRange(refUnitBuilder);
        combinedUnits.AddRange(userUnits);

        var compilation = new MistCompilation(combinedUnits.ToImmutable(), options.EntryPoint, options.OutputType)
        {
            Projects = refProjectBuilder.ToImmutable(),
            HasStandardLibrary = !stdlibUnits.IsEmpty,
            GlobalUsings = manifest.GlobalUsings,
        };

        return new LoadResult(
            compilation,
            refConfigurationBuilder.ToImmutable(),
            manifest.References.ExternalAssemblies,
            ComposedProjects(options, resolved),
            manifest.References.BuiltCSharpProjectCount);
    }

    /// <summary>
    /// Null when the manifest or its references fail. Tests and CLI invocations often supply a project folder
    /// without a <c>mist.toml</c>.
    /// </summary>
    private static ManifestLoad? ReadManifest(CompilerOptions options, DiagnosticBag allDiagnostics)
    {
        var globalUsings = options.GlobalUsings.IsDefault ? ImmutableArray<string>.Empty : options.GlobalUsings;
        var none = new LoadedReferences([], [], 0);

        if(string.IsNullOrEmpty(options.ProjectFolder)
            || !File.Exists(Path.Combine(options.ProjectFolder, ProjectFileParser.FileName)))
        {
            return new ManifestLoad(globalUsings, none);
        }

        var parseBag = new DiagnosticBag(allDiagnostics.Operation);
        var projectConfig = options.Configuration ?? ProjectFileParser.ParseRoot(options.ProjectFolder, parseBag);

        foreach(var d in parseBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        if(parseBag.HasErrors)
        {
            return null;
        }

        if(projectConfig is null)
        {
            return new ManifestLoad(globalUsings, none);
        }

        globalUsings = projectConfig.GlobalUsings.IsDefault ? ImmutableArray<string>.Empty : projectConfig.GlobalUsings;
        if(projectConfig.References.IsEmpty)
        {
            return new ManifestLoad(globalUsings, none);
        }

        var resolveBag = new DiagnosticBag(allDiagnostics.Operation);
        var references = ProjectReferenceLoader.Load(
            options.ProjectFolder, projectConfig.References, options.TargetFramework, resolveBag);

        foreach(var d in resolveBag.ToImmutableArray())
        {
            allDiagnostics.Add(d);
        }

        return resolveBag.HasErrors ? null : new ManifestLoad(globalUsings, references);
    }

    private static ImmutableArray<ComposedProject> ComposedProjects(
        CompilerOptions options, ImmutableArray<ResolvedProjectReference> references)
    {
        var rootManifest = string.IsNullOrEmpty(options.ProjectFolder)
            ? ""
            : Path.GetFullPath(Path.Combine(options.ProjectFolder, ProjectFileParser.FileName));

        return
        [
            new ComposedProject(options.ProjectName, rootManifest),
            .. references.Select(reference => new ComposedProject(reference.Configuration.Name, reference.CanonicalTomlPath)),
        ];
    }

    private sealed record LexResult(
        Dictionary<CompilationUnit, TokenStream> TokenStreams,
        ImmutableArray<TokenStream>.Builder? StdlibTokensForCache,
        StdlibParseCache.Entry? CachedStdlib);

    private LexResult Lex(MistCompilation compilation, DiagnosticBag allDiagnostics)
    {
        var lexingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("2", "Lexing");

        var cachedStdlib = stdlibParseCache.TryGet(compilation.Units.Count(u => u.IsStdlib));

        var tokenStreams = new Dictionary<CompilationUnit, TokenStream>();
        var stdlibTokensForCache = cachedStdlib is not null
            ? null
            : ImmutableArray.CreateBuilder<TokenStream>();

        foreach(var unit in compilation.Units)
        {
            if(unit.IsStdlib && cachedStdlib is not null)
            {
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

        // The framework references let a package assembly the framework already provides be skipped.
        var packageResult = PackageResolver.ResolvePackages(
            options.Packages, options.FrameworkReferences, options.TargetFramework, resolutionDiagnostics.Reported,
            resolutionDiagnostics.Deliver, runtimeIdentifier);

        var frameworkResult = FrameworkReferenceResolver.Resolve(
            options.FrameworkReferences, options.TargetFramework, resolutionDiagnostics.Reported);
        resolutionDiagnostics.Deliver();

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

        if(options.VerboseResolution)
        {
            var details = new List<string>();

            foreach(var pkg in packageResult.Resolved)
            {
                details.Add($"Restored {pkg.Name} {pkg.Version} ({pkg.SelectedTfm})");
            }

            foreach(var fw in frameworkResult.Resolved)
            {
                details.Add($"Resolved {fw.Name} {fw.Version}");
            }

            foreach(var pkg in packageResult.Resolved)
            {
                if(pkg.AssemblyCount == 0)
                {
                    details.Add($"{pkg.Name} contributed no assemblies for {options.TargetFramework}");
                }
            }

            details.Add($"{(int)resolutionStopwatch.Elapsed.TotalMilliseconds} ms");

            var successCount = packageResult.Resolved.Length + frameworkResult.Resolved.Length;
            var failureCountTotal = packageResult.FailureCount + frameworkResult.FailureCount;
            details.Add($"{successCount} succeeded, {failureCountTotal} failed");

            progressReporter.ReportStageComplete("1.5", resolutionStopwatch.Elapsed, details);
        }

        // A resolution failure continues with whatever resolved (Req 2.6, 5.8).
        return (resolvedAssemblyPaths, packageResult);
    }
}