using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Diagnostics follow the summary, unlike a build's, because no later stage runs
/// (compiler-progress-output Requirement 12.12).
/// </summary>
public static class DependencyRestore
{
    public static int Run(ProjectConfiguration configuration, TextWriter output, TextWriter error)
    {
        var diagnostics = new DiagnosticBag();
        var packages = PackageResolver.ResolvePackages(
            configuration.Packages, configuration.FrameworkReferences, configuration.TargetFramework, diagnostics);
        var frameworks = FrameworkReferenceResolver.Resolve(
            configuration.FrameworkReferences, configuration.TargetFramework, diagnostics);

        return Report(packages, frameworks, diagnostics.ToImmutableArray(), configuration.TargetFramework, output, error);
    }

    public static int Run(string projectFolder, ProjectConfiguration configuration, TextWriter output, TextWriter error)
    {
        var referenceErrors = RestoreReferences(projectFolder, configuration.References, output, DotnetRestoreProcess.Run);
        var exitCode = Run(configuration, output, error);

        foreach(var line in referenceErrors)
        {
            error.WriteLine(line);
        }

        return referenceErrors.Count > 0 ? 1 : exitCode;
    }

    internal static List<string> RestoreReferences(
        string projectFolder,
        ImmutableArray<ProjectReference> references,
        TextWriter output,
        Func<string, string, (int ExitCode, string Stdout, string Stderr)> restore)
    {
        var errors = new List<string>();
        if(references.IsDefaultOrEmpty)
        {
            return errors;
        }

        var diagnostics = new DiagnosticBag();
        var resolution = ProjectReferenceResolver.ResolveAll(projectFolder, references, diagnostics);

        foreach(var project in resolution.Projects)
        {
            output.WriteLine($"Reference {project.Configuration.Name}: Mist project {project.CanonicalTomlPath}");
        }

        foreach(var reference in resolution.ExternalReferences.DistinctBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine($"Reference {reference.Name}: {ProjectReferenceResolver.Describe(reference.Kind)} {reference.Path}");
            if(reference.Kind != ReferenceKind.CSharpProject)
            {
                continue;
            }

            var (exitCode, stdout, stderr) = restore(reference.Path, Path.GetDirectoryName(reference.Path)!);
            if(exitCode != 0)
            {
                var reason = CSharpProjectBuilder.ErrorLines(new DotnetOutcome(exitCode, stdout, stderr, TimedOut: false), "dotnet restore");
                errors.Add($"error: dotnet restore of '{reference.Path}' for reference '{reference.Name}' failed: {reason}");
            }
        }

        errors.AddRange(diagnostics.ToImmutableArray().Select(DiagnosticFormatter.Format));
        return errors;
    }

    internal static int Report(
        PackageResolutionResult packages,
        FrameworkResolutionResult frameworks,
        ImmutableArray<Diagnostic> diagnostics,
        string targetFramework,
        TextWriter output,
        TextWriter error)
    {
        foreach(var package in packages.Resolved)
        {
            output.WriteLine($"Restored {package.Name} {package.Version} ({package.SelectedTfm})");
        }

        foreach(var framework in frameworks.Resolved)
        {
            output.WriteLine($"Resolved {framework.Name} {framework.Version}");
        }

        foreach(var package in packages.Resolved.Where(package => package.AssemblyCount == 0))
        {
            output.WriteLine($"{package.Name} contributed no assemblies for {targetFramework}");
        }

        var failures = packages.FailureCount + frameworks.FailureCount;
        output.WriteLine($"{packages.Resolved.Length + frameworks.Resolved.Length} succeeded, {failures} failed");

        foreach(var diagnostic in diagnostics)
        {
            error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        return failures > 0 ? 1 : 0;
    }
}