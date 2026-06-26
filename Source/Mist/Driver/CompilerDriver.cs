using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
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
    private static (ImmutableArray<TokenStream> Tokens, ImmutableArray<Storm.Runtime.SyntaxNode> Asts)? stdlibCache;
    private static readonly object stdlibCacheLock = new();

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
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));

            return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
        }

        // Stage 1: Loading
        var loadingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("1", "Loading");

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
                    Location: default,
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
                        foreach(var proj in resolved)
                        {
                            var projGlobalUsings = proj.Configuration.GlobalUsings.IsDefault
                                ? ImmutableArray<string>.Empty
                                : proj.Configuration.GlobalUsings;

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
                                        GlobalUsings = projGlobalUsings,
                                    });
                                }
                                catch(Exception ex)
                                {
                                    allDiagnostics.Add(new Diagnostic(
                                        new DiagnosticId("MIST_FILE_ERROR"),
                                        DiagnosticSeverity.Error,
                                        PipelineStage.Driver,
                                        $"Failed to read referenced source file '{path}': {ex.Message}",
                                        Location: default,
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
            }
            else if(refParseBag.HasErrors)
            {
                return new CompilerResult(false, null, allDiagnostics.ToImmutableArray());
            }
        }

        // Apply global usings to user compilation units.
        if(!userGlobalUsings.IsEmpty)
        {
            for(var i = 0; i < userUnits.Count; i++)
            {
                userUnits[i] = userUnits[i] with { GlobalUsings = userGlobalUsings };
            }
        }

        // Step 1d: Combine units
        // Order: stdlib → referenced project units (deepest first) → user units
        var combinedUnits = ImmutableArray.CreateBuilder<CompilationUnit>(
            stdlibUnits.Length + refUnits.Length + userUnits.Count);
        combinedUnits.AddRange(stdlibUnits);
        combinedUnits.AddRange(refUnits);
        combinedUnits.AddRange(userUnits);

        var compilation = new MistCompilation(combinedUnits.ToImmutable(), options.EntryPoint);

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

        // Stage 1.5: Package Resolution (runs only when deps are declared)
        var resolvedAssemblyPaths = ImmutableArray<string>.Empty;

        var hasPackages = !options.Packages.IsDefaultOrEmpty;
        var hasFrameworkRefs = !options.FrameworkReferences.IsDefaultOrEmpty;

        if(hasPackages || hasFrameworkRefs)
        {
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

            resolvedAssemblyPaths = [.. mergedPaths];

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
        }

        // Stage 2: Lexing
        var lexingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("2", "Lexing");

        // Try to retrieve cached stdlib lex/parse results.
        var cachedStdlib = stdlibCache;

        var tokenStreams = new Dictionary<CompilationUnit, TokenStream>();
        var stdlibTokensForCache = cachedStdlib.HasValue
            ? null
            : ImmutableArray.CreateBuilder<TokenStream>();

        foreach(var unit in compilation.Units)
        {
            if(unit.IsStdlib && cachedStdlib.HasValue)
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
        if(cachedStdlib.HasValue)
        {
            totalTokens += cachedStdlib.Value.Tokens.Sum(ts => ts.DefaultChannelTokens.Length);
        }

        progressReporter.ReportStageComplete("2", lexingStopwatch.Elapsed, new[]
        {
            $"{totalTokens} tokens",
        });

        // Stage 3: Parsing
        var parsingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("3", "Parsing");

        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();
        var stdlibAstsForCache = cachedStdlib.HasValue
            ? null
            : ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>();

        // If we have cached stdlib ASTs, prepend them first.
        if(cachedStdlib.HasValue)
        {
            asts.AddRange(cachedStdlib.Value.Asts);
        }

        var unitsWithTopLevelStatements = new HashSet<CompilationUnit>();

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
                parseResult.Root.SetParentLinks();
                asts.Add(parseResult.Root);
                if(unit.IsStdlib)
                {
                    stdlibAstsForCache?.Add(parseResult.Root);
                }

                // Detect top-level statements in the parsed AST.
                if(parseResult.Root is CompilationUnitNode cu
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
        if(!cachedStdlib.HasValue && stdlibTokensForCache is not null && stdlibAstsForCache is not null)
        {
            lock(stdlibCacheLock)
            {
                if(!stdlibCache.HasValue)
                {
                    stdlibCache = (stdlibTokensForCache.ToImmutable(), stdlibAstsForCache.ToImmutable());
                }
            }
        }

        parsingStopwatch.Stop();
        var totalNodes = asts.Sum(root => SyntaxTreeMetrics.CountNodes(root));
        progressReporter.ReportStageComplete("3", parsingStopwatch.Elapsed, new[]
        {
            $"{totalNodes} AST nodes",
        });

        // Stage 4: Semantic Model
        var semanticStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("4", "Semantic Model");

        var ambientTypeResolver = backend.GetAmbientTypeResolver(options.TargetFramework, resolvedAssemblyPaths);
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
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));
            semanticModel = null;
        }

        semanticStopwatch.Stop();
        var topLevelConstructs = semanticModel?.AllSymbols.Count(s =>
            s.Origin == SymbolOrigin.UserCode &&
            s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract) ?? 0;
        progressReporter.ReportStageComplete("4", semanticStopwatch.Elapsed, new[]
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

        // Stage 4c.5: IClonable Derivation (Ownership v2, Phase 2.5)
        // Computes ImplementsIClonable for every user and bootstrap class/struct
        // type — true if the type explicitly declares IClonable<T> or every field's
        // type implements IClonable<T> (recursive, cycle-safe greatest fixpoint).
        // Must run after the semantic model is built and before the OwnershipAnalyzer
        // (Stage 5) which consults the cached result via MoveBoundaryResolver.
        IClonableDerivationEngine.RunInference(semanticModel);

        // Stage 4d: Synthetic Drop Inference
        // Synthesizes a void Drop() for types that have droppable fields but no
        // explicit Drop(), cascading the obligation transitively in dependency
        // order. Runs after the semantic model is built because it reads
        // the finalized IsOwnedType / Discipline state.
        SyntheticDropInferenceEngine.RunInference(semanticModel);

        // Stage 5: Analysis
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("5", "Analysis");

        var filteredAnalyzers = options.EnabledAnalyzerIds.IsEmpty
            ? analyzers
            : analyzers.Where(a => options.EnabledAnalyzerIds.Contains(a.AnalyzerId)).ToImmutableArray();

        var runner = new AnalyzerRunner(filteredAnalyzers);
        var analyzerResult = runner.RunAll(semanticModel);

        analysisStopwatch.Stop();
        var analyzerDetails = analyzerResult.Timings
            .Select(entry => $"{entry.AnalyzerId}  {entry.Elapsed.TotalSeconds:F2}s")
            .ToArray();
        progressReporter.ReportStageComplete("5", analysisStopwatch.Elapsed, analyzerDetails);

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
        progressReporter.ReportStageStart("6", "Emit");

        var backendBag = new DiagnosticBag();
        var projectName = options.ProjectName;

        IReadOnlyDictionary<string, string>? emittedFiles = null;
        BackendResult backendResult;

        if(options.EmitToDisk)
        {
            backendResult = backend.Emit(semanticModel, options.OutputDirectory, options.BinDirectory, projectName, backendBag);

            // Patch the emitted .csproj with declared NuGet packages and framework references
            // so the downstream C# compilation can resolve the referenced types.
            if(backendResult.Succeeded && (hasPackages || hasFrameworkRefs))
            {
                PatchCsprojWithReferences(options.OutputDirectory, projectName, options.Packages, options.FrameworkReferences);
            }

            // Patch the emitted .csproj with user-declared MSBuild properties from [csproj_properties].
            if(backendResult.Succeeded)
            {
                PatchCsprojWithProperties(options.OutputDirectory, projectName, options.CsprojProperties);
            }
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
        if(backendResult.PrimaryArtifactPath is not null)
        {
            emitDetails.Add(backendResult.PrimaryArtifactPath);
        }

        progressReporter.ReportStageComplete("6", emitStopwatch.Elapsed, emitDetails);

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

    /// <summary>
    /// Patches the emitted .csproj file with a custom <c>&lt;PropertyGroup&gt;</c> containing
    /// user-declared MSBuild properties from the <c>[csproj_properties]</c> table. The new
    /// PropertyGroup is inserted immediately after the first <c>&lt;/PropertyGroup&gt;</c> tag
    /// (i.e., after the hardcoded compiler-managed PropertyGroup, before any ItemGroups).
    /// Values are XML-escaped via <see cref="System.Security.SecurityElement.Escape"/>.
    /// </summary>
    private static void PatchCsprojWithProperties(
        string outputDirectory,
        string projectName,
        ImmutableArray<CsprojProperty> csprojProperties)
    {
        if(csprojProperties.IsDefaultOrEmpty)
        {
            return;
        }

        var csprojPath = Path.Combine(outputDirectory, $"{projectName}.csproj");
        if(!File.Exists(csprojPath))
        {
            return;
        }

        var content = File.ReadAllText(csprojPath);

        // Build the custom PropertyGroup fragment.
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine();
        _ = sb.AppendLine("  <PropertyGroup>");
        foreach(var prop in csprojProperties)
        {
            var escapedValue = System.Security.SecurityElement.Escape(prop.Value) ?? prop.Value;
            _ = sb.AppendLine($"    <{prop.Key}>{escapedValue}</{prop.Key}>");
        }

        _ = sb.AppendLine("  </PropertyGroup>");

        // Insert after the first </PropertyGroup> tag.
        const string marker = "</PropertyGroup>";
        var insertionPoint = content.IndexOf(marker, StringComparison.Ordinal);
        if(insertionPoint >= 0)
        {
            insertionPoint += marker.Length;
            content = content.Insert(insertionPoint, sb.ToString());
            File.WriteAllText(csprojPath, content);
        }
    }

    /// <summary>
    /// Patches the emitted .csproj file with PackageReference and FrameworkReference entries
    /// so the downstream C# compilation resolves types from declared packages and frameworks.
    /// </summary>
    private static void PatchCsprojWithReferences(
        string outputDirectory,
        string projectName,
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences)
    {
        var csprojPath = Path.Combine(outputDirectory, $"{projectName}.csproj");
        if(!File.Exists(csprojPath))
        {
            return;
        }

        var content = File.ReadAllText(csprojPath);

        // Build the additional ItemGroup entries.
        var sb = new System.Text.StringBuilder();

        if(!packages.IsDefaultOrEmpty)
        {
            _ = sb.AppendLine("  <ItemGroup>");
            foreach(var pkg in packages)
            {
                _ = sb.AppendLine($"    <PackageReference Include=\"{pkg.Name}\" Version=\"{pkg.Version}\" />");
            }

            _ = sb.AppendLine("  </ItemGroup>");
        }

        if(!frameworkReferences.IsDefaultOrEmpty)
        {
            _ = sb.AppendLine("  <ItemGroup>");
            foreach(var fw in frameworkReferences)
            {
                _ = sb.AppendLine($"    <FrameworkReference Include=\"{fw.Name}\" />");
            }

            _ = sb.AppendLine("  </ItemGroup>");
        }

        if(sb.Length > 0)
        {
            // Insert before the closing </Project> tag.
            var insertionPoint = content.LastIndexOf("</Project>", StringComparison.Ordinal);
            if(insertionPoint >= 0)
            {
                content = content.Insert(insertionPoint, sb.ToString());
                File.WriteAllText(csprojPath, content);
            }
        }
    }
}