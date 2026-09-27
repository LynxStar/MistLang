using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

public static class ProjectReferenceResolver
{
    /// <summary>
    /// Resolves named <c>[references]</c> in dependency-first order. Manifest loads,
    /// including failures, are shared per canonical path; names are validated per edge.
    /// The result retains resolved siblings and dependents when errors are reported;
    /// callers must check <see cref="DiagnosticBag.HasErrors"/> before compiling.
    /// </summary>
    public static ImmutableArray<ResolvedProjectReference> Resolve(
        string rootProjectFolder,
        ImmutableArray<ProjectReference> references,
        DiagnosticBag diagnostics)
    {
        return Resolve(rootProjectFolder, references, diagnostics, LoadManifest, FindSourcePaths);
    }

    internal static ImmutableArray<ResolvedProjectReference> Resolve(
        string rootProjectFolder,
        ImmutableArray<ProjectReference> references,
        DiagnosticBag diagnostics,
        Func<string, DiagnosticBag, ProjectConfiguration?> loadManifest,
        Func<string, ImmutableArray<string>> findSourcePaths)
    {
        var loads = new Dictionary<string, ProjectConfiguration?>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedProjectReference>();
        var rootCanonicalTomlPath = Path.GetFullPath(
            Path.Combine(rootProjectFolder, ProjectFileParser.FileName));
        _ = inProgress.Add(rootCanonicalTomlPath);
        var chain = new List<string> { rootCanonicalTomlPath };

        foreach(var reference in references)
        {
            visit(reference);
        }

        return result.ToImmutableArray();

        void visit(ProjectReference reference)
        {
            var canonicalTomlPath = reference.Path;
            if(canonicalTomlPath == rootCanonicalTomlPath)
            {
                reportCycle(canonicalTomlPath);
                return;
            }

            if(!loads.TryGetValue(canonicalTomlPath, out var config))
            {
                config = loadManifest(canonicalTomlPath, diagnostics);
                loads.Add(canonicalTomlPath, config);
            }

            if(config is null)
            {
                return;
            }

            if(!string.Equals(config.Name, reference.Name, StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DriverDiagnostics.ProjectReferenceNameMismatch.Id,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Reference key '{reference.Name}' does not match the target project's name '{config.Name}' in '{canonicalTomlPath}'.",
                    Location: default,
                    CompilationUnitPath: canonicalTomlPath,
                    AnalyzerName: null));
                return;
            }

            if(inProgress.Contains(canonicalTomlPath))
            {
                reportCycle(canonicalTomlPath);
                return;
            }

            if(visited.Contains(canonicalTomlPath))
            {
                return;
            }

            _ = inProgress.Add(canonicalTomlPath);
            chain.Add(canonicalTomlPath);

            foreach(var transRef in config.References)
            {
                visit(transRef);
            }

            var projectFolder = Path.GetDirectoryName(canonicalTomlPath)!;
            result.Add(new ResolvedProjectReference(
                canonicalTomlPath,
                projectFolder,
                config,
                findSourcePaths(projectFolder)));

            _ = visited.Add(canonicalTomlPath);
            _ = inProgress.Remove(canonicalTomlPath);
            chain.RemoveAt(chain.Count - 1);
        }

        void reportCycle(string canonicalTomlPath)
        {
            var cycleStart = chain.IndexOf(canonicalTomlPath);
            var cycleNodes = chain.Skip(cycleStart).ToList();
            cycleNodes.Add(canonicalTomlPath);
            var cycleChain = string.Join(" → ", cycleNodes);

            diagnostics.Add(new Diagnostic(
                DriverDiagnostics.ProjectCircularReference.Id,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Circular project reference detected: {cycleChain}.",
                Location: default,
                CompilationUnitPath: canonicalTomlPath,
                AnalyzerName: null));
        }
    }

    private static ProjectConfiguration? LoadManifest(string canonicalTomlPath, DiagnosticBag diagnostics)
    {
        if(!File.Exists(canonicalTomlPath))
        {
            var directory = Path.GetDirectoryName(canonicalTomlPath);
            if(directory is not null
                && Directory.Exists(directory)
                && Path.GetFileName(canonicalTomlPath)
                       .Equals(ProjectFileParser.FileName, StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DriverDiagnostics.ProjectReferenceNoProjectFile.Id,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Referenced directory '{directory}' does not contain a 'mist.toml' file.",
                    Location: default,
                    CompilationUnitPath: canonicalTomlPath,
                    AnalyzerName: null));
            }
            else
            {
                diagnostics.Add(new Diagnostic(
                    DriverDiagnostics.ProjectReferenceNotFound.Id,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Referenced project path '{canonicalTomlPath}' does not exist.",
                    Location: default,
                    CompilationUnitPath: canonicalTomlPath,
                    AnalyzerName: null));
            }

            return null;
        }

        if(!Path.GetFileName(canonicalTomlPath)
                 .Equals(ProjectFileParser.FileName, StringComparison.Ordinal))
        {
            diagnostics.Add(new Diagnostic(
                DriverDiagnostics.ProjectReferenceNotFound.Id,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Referenced project path '{canonicalTomlPath}' does not exist.",
                Location: default,
                CompilationUnitPath: canonicalTomlPath,
                AnalyzerName: null));
            return null;
        }

        var projectFolder = Path.GetDirectoryName(canonicalTomlPath)!;
        var parseBag = new DiagnosticBag();
        var config = ProjectFileParser.Parse(projectFolder, parseBag);

        foreach(var d in parseBag.ToImmutableArray())
        {
            diagnostics.Add(d);
        }

        return config;
    }

    private static ImmutableArray<string> FindSourcePaths(string projectFolder)
    {
        var sourcePaths = Directory.GetFiles(projectFolder, "*.mist", SearchOption.AllDirectories);
        Array.Sort(sourcePaths, StringComparer.Ordinal);
        return sourcePaths.ToImmutableArray();
    }
}