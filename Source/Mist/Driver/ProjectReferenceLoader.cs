using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;

namespace Mist.Driver;

public static class ProjectReferenceLoader
{
    public static LoadedReferences Load(
        string projectFolder,
        ImmutableArray<ProjectReference> references,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        var resolution = ProjectReferenceResolver.ResolveAll(projectFolder, references, diagnostics);
        if(diagnostics.HasErrors || resolution.ExternalReferences.IsEmpty)
        {
            return new LoadedReferences(resolution.Projects, [], 0);
        }

        var manifestPath = Path.GetFullPath(Path.Combine(projectFolder, ProjectFileParser.FileName));
        var (assemblies, builtCount) = CSharpProjectBuilder.BuildAll(
            resolution.ExternalReferences, targetFramework, manifestPath, diagnostics);

        return new LoadedReferences(resolution.Projects, assemblies, builtCount);
    }

    public static LoadedReferences Load(string projectFolder, ProjectConfiguration configuration, DiagnosticBag diagnostics,
        ICompilationProgressReporter? progress = null,
        ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>>? generated = null)
    {
        return Load(projectFolder, configuration, diagnostics, progress ?? NullProgressReporter.Instance,
            generated ?? ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>>.Empty, new GeneratorServices());
    }

    internal static LoadedReferences Load(string projectFolder, ProjectConfiguration configuration, DiagnosticBag diagnostics,
        ICompilationProgressReporter progress, ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>> generated,
        GeneratorServices services)
    {
        var resolution = ProjectReferenceResolver.ResolveAll(projectFolder, configuration.References, diagnostics);
        if(diagnostics.HasErrors)
        {
            return new LoadedReferences(resolution.Projects, [], 0);
        }

        var overlay = GeneratorPreparation.Prepare(projectFolder, configuration, resolution, diagnostics, progress, generated, services);
        if(diagnostics.HasErrors)
        {
            return new LoadedReferences([], [], 0);
        }

        var configurations = resolution.Projects.ToDictionary(project => project.CanonicalTomlPath, project => project.Configuration, StringComparer.Ordinal);
        if(!overlay.IsEmpty)
        {
            resolution = ProjectReferenceResolver.ResolveAll(projectFolder, configuration.References, diagnostics,
                (path, bag) => configurations.TryGetValue(path, out var known) ? known : ProjectReferenceResolver.LoadManifest(path, bag),
                ProjectReferenceResolver.EnumerateSources, overlay);
        }

        resolution = resolution with
        {
            Projects = [.. resolution.Projects.Select(project => project.Configuration.SeedSources is { } seed
            ? project with { SourcePaths = [.. project.SourcePaths.Where(path => !SourcePathSelection.IsWithin(Path.GetFullPath(path), seed.Path))] }
            : project)]
        };
        if(diagnostics.HasErrors || resolution.ExternalReferences.IsEmpty)
        {
            return new LoadedReferences(resolution.Projects, [], 0);
        }

        var manifest = Path.GetFullPath(Path.Combine(projectFolder, ProjectFileParser.FileName));
        var chain = BuildChain.Format(BuildChain.Extend(BuildChain.Current(), manifest));
        var (assemblies, count) = CSharpProjectBuilder.BuildAll(resolution.ExternalReferences, configuration.TargetFramework, chain, diagnostics, services.Run);
        return new LoadedReferences(resolution.Projects, assemblies, count);
    }

}