using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;

namespace Mist.Driver;

public sealed partial class CompilerDriver
{
    private sealed record LoadResult(
        MistCompilation Compilation,
        ImmutableArray<ProjectConfiguration> ReferencedConfigurations,
        ImmutableArray<ExternalAssembly> ExternalAssemblies,
        ImmutableArray<ComposedProject> ComposedProjects,
        int BuiltCSharpProjectCount,
        ImmutableArray<ClaimedInput> ClaimedInputs);

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
        IBackend backend,
        string runtimeIdentifier,
        DiagnosticBag allDiagnostics)
    {
        var loadingStopwatch = Stopwatch.StartNew();
        progressReporter.ReportStageStart("1", "Loading");

        var loadResult = ReadCompilationUnits(options, backend, allDiagnostics);

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
        IBackend backend,
        DiagnosticBag allDiagnostics)
    {
        var manifest = ReadManifest(options, backend, allDiagnostics);
        if(manifest is null)
        {
            return null;
        }

        var userUnits = ImmutableArray.CreateBuilder<CompilationUnit>();
        var claimedInputs = ImmutableArray.CreateBuilder<ClaimedInput>();
        var rootProject = new MistProject(options.NamespaceRoot, options.ProjectFolder,
            Path.GetFullPath(Path.Combine(options.ProjectFolder, ProjectFileParser.FileName)))
        {
            GlobalUsings = manifest.GlobalUsings,
            EntryPoint = options.EntryPoint,
            OutputType = options.OutputType,
        };
        foreach(var path in options.SourcePaths.Where(path => Path.GetExtension(path).Equals(".mist", StringComparison.OrdinalIgnoreCase)
            || backend.ClaimedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                var contents = File.ReadAllText(path);
                var sourceText = new SourceText(path, contents);
                if(Path.GetExtension(path).Equals(".mist", StringComparison.OrdinalIgnoreCase))
                {
                    userUnits.Add(new CompilationUnit(sourceText, options.NamespaceRoot, options.ProjectFolder));
                }
                else if(backend.ClaimedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                {
                    claimedInputs.Add(new ClaimedInput(sourceText, rootProject));
                }
            }
            catch(Exception ex)
            {
                allDiagnostics.ReportTyped(DriverDiagnostics.FileError, new FileOnly(path), new PathValue(path), new TextValue(ex.Message));
            }
        }

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

            var projGlobalUsings = proj.Configuration.GlobalUsings.IsDefault
                ? ImmutableArray<string>.Empty
                : proj.Configuration.GlobalUsings;

            var referencedProject = new MistProject(
                proj.Configuration.NamespaceRoot,
                proj.ProjectFolder,
                proj.CanonicalTomlPath)
            {
                GlobalUsings = projGlobalUsings,
                EntryPoint = proj.Configuration.EntryPoint,
                OutputType = proj.Configuration.OutputType,
            };
            refProjectBuilder.Add(referencedProject);

            foreach(var path in proj.SourcePaths)
            {
                try
                {
                    var contents = File.ReadAllText(path);
                    var sourceText = new SourceText(path, contents);
                    if(Path.GetExtension(path).Equals(".mist", StringComparison.OrdinalIgnoreCase))
                    {
                        refUnitBuilder.Add(new CompilationUnit(sourceText, proj.Configuration.NamespaceRoot, proj.ProjectFolder)
                        {
                            OriginProjectPath = proj.CanonicalTomlPath,
                        });
                    }
                    else if(backend.ClaimedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    {
                        claimedInputs.Add(new ClaimedInput(sourceText, referencedProject));
                    }
                }
                catch(Exception ex)
                {
                    allDiagnostics.ReportTyped(DriverDiagnostics.FileError, new FileOnly(path), new PathValue(path), new TextValue(ex.Message));
                }
            }
        }

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
            manifest.References.BuiltCSharpProjectCount,
            claimedInputs.ToImmutable());
    }

    private static ManifestLoad? ReadManifest(CompilerOptions options, IBackend backend, DiagnosticBag allDiagnostics)
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
            options.ProjectFolder, projectConfig.References, options.TargetFramework, resolveBag, backend.ClaimedExtensions);

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

}