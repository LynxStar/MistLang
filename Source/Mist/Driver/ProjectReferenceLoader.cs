using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

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
}