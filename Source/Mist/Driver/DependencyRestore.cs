using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// <c>mist restore</c>: Stage 1.5's two resolvers run alone, with no driver and no lexing, and their
/// detail is printed whatever the verbosity (csharp-interop-v2 Requirement 13). With no later stage to
/// wait for, every diagnostic follows the summary.
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