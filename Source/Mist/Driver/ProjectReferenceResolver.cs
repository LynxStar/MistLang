using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Resolves the <c>[[references]]</c> graph declared in a project's <c>mist.toml</c>,
/// performing cycle detection, deduplication, and topological ordering.
/// Returns a flat list of <see cref="ResolvedProjectReference"/> values ordered
/// deepest-dependency-first, ready for <see cref="CompilationUnit"/> loading.
/// </summary>
public static class ProjectReferenceResolver
{
    /// <summary>
    /// Resolves the given project references starting from
    /// <paramref name="rootProjectFolder"/>, performing DFS-based cycle
    /// detection, deduplication, and topological ordering.
    /// </summary>
    /// <param name="rootProjectFolder">
    /// The folder of the root (user) project. Its canonical path is added to
    /// the <c>inProgress</c> set so that self-references are detected.
    /// </param>
    /// <param name="references">
    /// The direct references declared by the root project.
    /// Each <see cref="ProjectReference.Path"/> is already normalized to an
    /// absolute <c>mist.toml</c> path by <see cref="ProjectFileParser"/>.
    /// </param>
    /// <param name="diagnostics">
    /// Accumulates all diagnostics encountered during resolution. The caller
    /// should check <see cref="DiagnosticBag.HasErrors"/> after the call.
    /// </param>
    /// <returns>
    /// A topologically ordered (deepest-dependency-first) list of resolved
    /// project references. Returns an empty array when errors are encountered.
    /// </returns>
    public static ImmutableArray<ResolvedProjectReference> Resolve(
        string rootProjectFolder,
        ImmutableArray<ProjectReference> references,
        DiagnosticBag diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedProjectReference>();

        // Add the root project's canonical toml path to inProgress so that
        // self-references (A → A) are detected as cycles.
        var rootCanonicalTomlPath = Path.GetFullPath(
            Path.Combine(rootProjectFolder, ProjectFileParser.FileName));
        inProgress.Add(rootCanonicalTomlPath);

        // Kick off DFS for each direct reference of the root project.
        var chain = new List<string> { rootCanonicalTomlPath };

        foreach (var reference in references)
        {
            Dfs(reference.Name, reference.Path, chain, visited, inProgress, result, diagnostics);
        }

        return result.ToImmutableArray();
    }

    private static void Dfs(
        string referenceName,
        string canonicalTomlPath,
        List<string> chain,
        HashSet<string> visited,
        HashSet<string> inProgress,
        List<ResolvedProjectReference> result,
        DiagnosticBag diagnostics)
    {
        // Already fully processed — deduplicate.
        if (visited.Contains(canonicalTomlPath))
        {
            return;
        }

        // Cycle detection: path is currently on the DFS stack.
        if (inProgress.Contains(canonicalTomlPath))
        {
            var cycleStart = chain.IndexOf(canonicalTomlPath);
            var cycleNodes = chain.Skip(cycleStart).ToList();
            cycleNodes.Add(canonicalTomlPath);
            var cycleChain = string.Join(" → ", cycleNodes);

            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectCircularReference,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Circular project reference detected: {cycleChain}.",
                Location: default,
                CompilationUnitPath: canonicalTomlPath,
                AnalyzerName: null));
            return;
        }

        // Existence check: the file must exist on disk.
        if (!File.Exists(canonicalTomlPath))
        {
            // Distinguish between "directory exists but no mist.toml" and
            // "path doesn't exist at all / points to a non-mist.toml file".
            var directory = Path.GetDirectoryName(canonicalTomlPath);
            if (directory is not null
                && Directory.Exists(directory)
                && Path.GetFileName(canonicalTomlPath)
                       .Equals(ProjectFileParser.FileName, StringComparison.Ordinal))
            {
                // The directory exists but has no mist.toml.
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.ProjectReferenceNoProjectFile,
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
                    DiagnosticId.ProjectReferenceNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Referenced project path '{canonicalTomlPath}' does not exist.",
                    Location: default,
                    CompilationUnitPath: canonicalTomlPath,
                    AnalyzerName: null));
            }

            return;
        }

        // The file exists — verify it is named mist.toml.
        if (!Path.GetFileName(canonicalTomlPath)
                 .Equals(ProjectFileParser.FileName, StringComparison.Ordinal))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceNotFound,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Referenced project path '{canonicalTomlPath}' does not exist.",
                Location: default,
                CompilationUnitPath: canonicalTomlPath,
                AnalyzerName: null));
            return;
        }

        // Mark as in-progress (on the DFS stack).
        inProgress.Add(canonicalTomlPath);
        chain.Add(canonicalTomlPath);

        // Parse the referenced project's mist.toml.
        var projectFolder = Path.GetDirectoryName(canonicalTomlPath)!;
        var parseBag = new DiagnosticBag();
        var config = ProjectFileParser.Parse(projectFolder, parseBag);

        // Propagate parse diagnostics to the outer bag.
        foreach (var d in parseBag.ToImmutableArray())
        {
            diagnostics.Add(d);
        }

        if (config is null)
        {
            inProgress.Remove(canonicalTomlPath);
            chain.RemoveAt(chain.Count - 1);
            return;
        }

        // Name-mismatch validation (Requirement 4)
        if (config.Name != referenceName)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceNameMismatch,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Reference key '{referenceName}' does not match the target project's name '{config.Name}' in '{canonicalTomlPath}'.",
                Location: default,
                CompilationUnitPath: canonicalTomlPath,
                AnalyzerName: null));

            inProgress.Remove(canonicalTomlPath);
            chain.RemoveAt(chain.Count - 1);
            return;
        }

        // Recurse into transitive references (post-order DFS).
        foreach (var transRef in config.References)
        {
            Dfs(transRef.Name, transRef.Path, chain, visited, inProgress, result, diagnostics);
        }

        // Post-order: add after all dependencies have been processed.
        var sourcePaths = Directory.GetFiles(projectFolder, "*.mist", SearchOption.AllDirectories);
        Array.Sort(sourcePaths, StringComparer.Ordinal);

        result.Add(new ResolvedProjectReference(
            canonicalTomlPath,
            projectFolder,
            config,
            sourcePaths.ToImmutableArray()));

        visited.Add(canonicalTomlPath);
        inProgress.Remove(canonicalTomlPath);
        chain.RemoveAt(chain.Count - 1);
    }
}
