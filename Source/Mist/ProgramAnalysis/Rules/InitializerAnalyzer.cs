using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// An initializer block assigns each member once (property-initializers Requirement 6), judged block by
/// block, so a nested construction's block answers only for itself. An entry whose target the binder
/// could not resolve to storage already has its complaint and is not counted.
/// </summary>
internal sealed class InitializerAnalyzer : IProgramAnalyzer
{
    public string Id => "object-initializer-blocks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is not NewExpression creation)
            {
                continue;
            }

            foreach(var repeated in Repeated(creation.Initializer))
            {
                context.Report(ProgramAnalysisDiagnostics.InitializerMemberRepeated, repeated.Node, [], repeated.MemberName!);
            }
        }
    }

    public static IEnumerable<ObjectInitializerAssignment> Repeated(IReadOnlyList<ObjectInitializerAssignment> block)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);

        foreach(var entry in block)
        {
            if(entry is { IsTargetFailed: false, TargetType: not null, MemberName: { } name } && !assigned.Add(name))
            {
                yield return entry;
            }
        }
    }
}