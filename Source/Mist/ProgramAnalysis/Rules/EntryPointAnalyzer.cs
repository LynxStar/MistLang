using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Referenced projects still answer to their own manifests: composing a build runs none of their
/// statements, so it cannot reveal their unreachable entry statements. Only the executable needs an entry point.
/// </summary>
internal sealed class EntryPointAnalyzer : IProgramAnalyzer
{
    public string Id => "entry-point-resolution";

    public void Analyze(AnalysisContext context)
    {
        foreach(var project in context.Projects)
        {
            switch(EntryPointSelection.Of(project))
            {
                case EntryPointSelection.Refused refused:
                    context.ReportAtProject(refused.Descriptor, project, [.. refused.Arguments]);
                    break;

                case EntryPointSelection.None when project.Kind == MistAssemblyKind.Self && !project.IsLibrary:
                    context.ReportAtProject(EntryPointDiagnostics.MissingEntryPoint, project, new NameValue(project.Name));
                    break;
            }
        }
    }
}