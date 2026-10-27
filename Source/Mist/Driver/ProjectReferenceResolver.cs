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
        return ResolveAll(rootProjectFolder, references, diagnostics).Projects;
    }

    public static ProjectReferenceResolution ResolveAll(
        string rootProjectFolder,
        ImmutableArray<ProjectReference> references,
        DiagnosticBag diagnostics)
    {
        return ResolveAll(rootProjectFolder, references, diagnostics, LoadManifest, EnumerateSources);
    }

    internal static ImmutableArray<ResolvedProjectReference> Resolve(
        string rootProjectFolder,
        ImmutableArray<ProjectReference> references,
        DiagnosticBag diagnostics,
        Func<string, DiagnosticBag, ProjectConfiguration?> loadManifest,
        Func<string, ImmutableArray<string>> enumerateSources)
    {
        return ResolveAll(rootProjectFolder, references, diagnostics, loadManifest, enumerateSources).Projects;
    }

    internal static ProjectReferenceResolution ResolveAll(
        string rootProjectFolder,
        ImmutableArray<ProjectReference> references,
        DiagnosticBag diagnostics,
        Func<string, DiagnosticBag, ProjectConfiguration?> loadManifest,
        Func<string, ImmutableArray<string>> enumerateSources)
    {
        var loads = new Dictionary<string, ProjectConfiguration?>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedProjectReference>();
        var externals = new List<ExternalReference>();
        var rootCanonicalTomlPath = Path.GetFullPath(
            Path.Combine(rootProjectFolder, ProjectFileParser.FileName));
        _ = inProgress.Add(rootCanonicalTomlPath);
        var chain = new List<string> { rootCanonicalTomlPath };
        var rootExternals = new List<ExternalReference>();

        foreach(var reference in references)
        {
            visit(reference, rootCanonicalTomlPath, rootExternals);
        }

        externals.AddRange(rootExternals);
        return new ProjectReferenceResolution(result.ToImmutableArray(), externals.ToImmutableArray());

        void visit(ProjectReference reference, string declaringManifest, List<ExternalReference> declared)
        {
            if(reference.IsDirectory)
            {
                if(ClassifyDirectory(reference, declaringManifest, diagnostics) is not { } classified)
                {
                    return;
                }

                if(classified.Kind != ReferenceKind.MistProject)
                {
                    Declare(reference.Name, classified.ResolvedPath, classified.Kind, declaringManifest, declared, diagnostics);
                    return;
                }

                reference = reference with { Path = classified.ResolvedPath, IsDirectory = false };
            }
            else if(reference.Kind != ReferenceKind.MistProject)
            {
                if(!File.Exists(reference.Path))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.ReferenceFileNotFound, new ProjectLocation(declaringManifest, null),
                        new NameValue(reference.Name), new PathValue(reference.Path));
                    return;
                }

                Declare(reference.Name, reference.Path, reference.Kind, declaringManifest, declared, diagnostics);
                return;
            }

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

            if(KeyMismatch(reference.Name, ReferenceKind.MistProject, config.Name, canonicalTomlPath) is { } mismatch)
            {
                diagnostics.Admit(mismatch);
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

            var own = new List<ExternalReference>();
            foreach(var transRef in config.References)
            {
                visit(transRef, canonicalTomlPath, own);
            }

            externals.AddRange(own);
            var projectFolder = Path.GetDirectoryName(canonicalTomlPath)!;
            result.Add(new ResolvedProjectReference(
                canonicalTomlPath,
                projectFolder,
                config,
                SourcePathSelection.ForReferencedProject(projectFolder, enumerateSources(projectFolder))));

            _ = visited.Add(canonicalTomlPath);
            _ = inProgress.Remove(canonicalTomlPath);
            chain.RemoveAt(chain.Count - 1);
        }

        void reportCycle(string canonicalTomlPath)
        {
            var cycleStart = chain.IndexOf(canonicalTomlPath);
            var cycleNodes = chain.Skip(cycleStart).ToList();
            cycleNodes.Add(canonicalTomlPath);
            diagnostics.ReportTyped(DriverDiagnostics.ProjectCircularReference, new ProjectLocation(canonicalTomlPath, null),
                new SequenceValue(SlotKind.Path, [.. cycleNodes.Select(path => (SlotValue)new PathValue(path))], Separator: " → "));
        }
    }

    /// <summary>
    /// The diagnostic for a reference key that differs from the name its target produces: a Mist
    /// project's <c>name</c>, or an assembly's or a built C# project's file name without extension.
    /// </summary>
    internal static Admission? KeyMismatch(string key, ReferenceKind kind, string producedName, string targetPath)
    {
        if(string.Equals(key, producedName, StringComparison.Ordinal))
        {
            return null;
        }

        var descriptor = DriverDiagnostics.ProjectReferenceNameMismatch;
        return DiagnosticFactory.Create(
            descriptor, kind == ReferenceKind.Assembly ? new FileOnly(targetPath) : new ProjectLocation(targetPath, null), PipelineStage.Driver,
            [new NameValue(key), new NameValue(producedName), new EnumValue(Describe(kind)), new PathValue(targetPath)]);
    }

    internal static string Describe(ReferenceKind kind)
    {
        return kind switch
        {
            ReferenceKind.CSharpProject => "C# project",
            ReferenceKind.Assembly => "assembly",
            _ => "Mist project",
        };
    }

    private static void Declare(
        string key, string path, ReferenceKind kind, string declaringManifest, List<ExternalReference> declared, DiagnosticBag diagnostics)
    {
        if(kind == ReferenceKind.Assembly
            && KeyMismatch(key, kind, Path.GetFileNameWithoutExtension(path), path) is { } mismatch)
        {
            diagnostics.Admit(mismatch);
            return;
        }

        declared.Add(new ExternalReference(key, path, kind, declaringManifest));
    }

    private static ReferenceClassified? ClassifyDirectory(ProjectReference reference, string declaringManifest, DiagnosticBag diagnostics)
    {
        var directory = Path.GetDirectoryName(reference.Path)!;
        if(!Directory.Exists(directory))
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceNotFound, new ProjectLocation(declaringManifest, null), new PathValue(directory));
            return null;
        }

        var fileNames = Directory.EnumerateFiles(directory).Select(file => Path.GetFileName(file)).ToList();
        switch(ReferenceClassifier.Classify(directory, isDirectory: true, fileNames))
        {
            case ReferenceClassified classified:
                return classified;
            case ReferenceAmbiguous ambiguous:
                diagnostics.ReportTyped(DriverDiagnostics.ReferenceAmbiguous, new ProjectLocation(declaringManifest, null),
                    new NameValue(reference.Name), new PathValue(directory),
                    new SequenceValue(SlotKind.Path, [.. ambiguous.Candidates.Select(path => (SlotValue)new PathValue(path))]));
                return null;
            default:
                diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceNoProjectFile, new ProjectLocation(declaringManifest, null), new PathValue(directory));
                return null;
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
                diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceNoProjectFile, new ProjectLocation(canonicalTomlPath, null), new PathValue(directory));
            }
            else
            {
                diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceNotFound, new ProjectLocation(canonicalTomlPath, null), new PathValue(canonicalTomlPath));
            }

            return null;
        }

        if(!Path.GetFileName(canonicalTomlPath)
                 .Equals(ProjectFileParser.FileName, StringComparison.Ordinal))
        {
            diagnostics.ReportTyped(DriverDiagnostics.ProjectReferenceNotFound, new ProjectLocation(canonicalTomlPath, null), new PathValue(canonicalTomlPath));
            return null;
        }

        var projectFolder = Path.GetDirectoryName(canonicalTomlPath)!;
        var parseBag = new DiagnosticBag(diagnostics.Operation);
        var config = ProjectFileParser.Parse(projectFolder, parseBag);

        foreach(var d in parseBag.ToImmutableArray())
        {
            diagnostics.Add(d);
        }

        return config;
    }

    internal static ImmutableArray<string> EnumerateSources(string projectFolder)
    {
        return [.. SourcePathSelection.EnumerateCandidates(projectFolder)];
    }
}