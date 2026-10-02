namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Every project in the compilation is judged against its own manifest, a referenced one included:
/// what it starts at when compiled by itself is a property of that project, and a build that composes
/// it runs none of its statements, so nowhere else would say they are unreachable.
/// </summary>
internal sealed class EntryPointAnalyzer : IProgramAnalyzer
{
    public string Id => "entry-point-resolution";

    public void Analyze(AnalysisContext context)
    {
        foreach(var project in context.Projects)
        {
            if(EntryPointSelection.Of(project) is EntryPointSelection.Refused refused)
            {
                context.ReportAtProject(refused.Descriptor, project, [.. refused.Arguments]);
            }
        }
    }
}