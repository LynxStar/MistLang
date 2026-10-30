using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Every project in the compilation is judged against its own manifest, a referenced one included:
/// what it starts at when compiled by itself is a property of that project, and a build that composes
/// it runs none of its statements, so nowhere else would say they are unreachable.
/// Only the compiled executable requires an entry point; referenced projects contribute declarations
/// without producing their own executable artifacts.
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