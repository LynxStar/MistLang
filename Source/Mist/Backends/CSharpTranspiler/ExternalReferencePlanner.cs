using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler;

internal static class ExternalReferencePlanner
{
    public static ReferencePlan Plan(
        string rootName,
        ImmutableArray<string> suppliedPaths,
        ImmutableArray<ExternalAssembly> externalAssemblies,
        ImmutableArray<ComposedProject> composedProjects,
        ImmutableArray<FrameworkReference> frameworkReferences,
        IAssemblyMetadataSource metadata,
        DiagnosticBag diagnostics)
    {
        var paths = suppliedPaths.IsDefaultOrEmpty ? new List<string>() : [.. suppliedPaths];
        if(externalAssemblies.IsDefaultOrEmpty)
        {
            return new ReferencePlan([.. paths], [], Failed: false);
        }

        var composed = composedProjects.IsDefaultOrEmpty ? [] : composedProjects;
        var shadowed = composed.Select(project => project.Name).Append(rootName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths)
        {
            _ = supplied.TryAdd(SimpleName(path), path);
        }

        var facadeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = false;
        foreach(var external in externalAssemblies)
        {
            foreach(var file in external.Closure.IsDefaultOrEmpty ? [external.Path] : external.Closure.Insert(0, external.Path))
            {
                var name = SimpleName(file);
                if(shadowed.Contains(name))
                {
                    continue;
                }

                if(supplied.TryGetValue(name, out var existing))
                {
                    failed |= existing != file && ReportConflict(existing, file, metadata, diagnostics);
                    continue;
                }

                supplied[name] = file;
                paths.Add(file);
            }

            failed |= ReportMissingFrameworks(external, frameworkReferences, metadata, diagnostics);

            foreach(var referenced in metadata.ReferencedNames(external.Path))
            {
                if(string.Equals(referenced, rootName, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Report(
                        DriverDiagnostics.ReferenceCycle,
                        default,
                        external.Path,
                        $"'{external.Name}' ({external.Path}) references '{rootName}', the project being compiled");
                    failed = true;
                }
                else if(composed.Any(project => string.Equals(project.Name, referenced, StringComparison.OrdinalIgnoreCase)))
                {
                    _ = facadeNames.Add(referenced);
                }
            }
        }

        return new ReferencePlan([.. paths], [.. composed.Where(project => facadeNames.Contains(project.Name))], failed);
    }

    private static bool ReportConflict(string existing, string file, IAssemblyMetadataSource metadata, DiagnosticBag diagnostics)
    {
        var suppliedVersion = metadata.AssemblyVersion(existing);
        var candidateVersion = metadata.AssemblyVersion(file);
        if(!AssemblyConflictRule.Conflicts((SimpleName(existing), suppliedVersion), (SimpleName(file), candidateVersion)))
        {
            return false;
        }

        diagnostics.Report(
            BackendDiagnostics.ReferenceAssemblyConflict,
            default,
            file,
            SimpleName(file),
            existing,
            suppliedVersion,
            file,
            candidateVersion);
        return true;
    }

    private static bool ReportMissingFrameworks(
        ExternalAssembly external,
        ImmutableArray<FrameworkReference> frameworkReferences,
        IAssemblyMetadataSource metadata,
        DiagnosticBag diagnostics)
    {
        if(external.RuntimeConfigPath is null || metadata.RuntimeConfig(external.RuntimeConfigPath) is not { } json)
        {
            return false;
        }

        var missing = RuntimeFrameworkRule.Missing(RuntimeFrameworkRule.Frameworks(json), frameworkReferences);
        foreach(var framework in missing)
        {
            diagnostics.Report(
                BackendDiagnostics.ReferenceFrameworkMissing, default, external.RuntimeConfigPath, external.Name, framework);
        }

        return missing.Length > 0;
    }

    private static string SimpleName(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }
}