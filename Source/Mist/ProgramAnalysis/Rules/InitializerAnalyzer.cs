using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Unresolved initializer targets belong to the binder's refusal and do not count as assigned storage.
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
                context.Report(ProgramAnalysisDiagnostics.InitializerMemberRepeated, repeated.Node, [], new NameValue(repeated.MemberName!));
            }

            foreach(var stray in Mixed(creation.Initializer))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.InitializerKindsMixed, stray.Node, [], new EnumValue(Does(stray)), new EnumValue(Does(creation.Initializer[0])));
            }
        }
    }

    public static IEnumerable<ObjectInitializerAssignment> Mixed(IReadOnlyList<ObjectInitializerAssignment> block)
    {
        return block.Where(entry => (entry.MemberName is null) != (block[0].MemberName is null));
    }

    private static string Does(ObjectInitializerAssignment entry)
    {
        return entry.MemberName is { } member ? $"assigns '{member}'" : "adds a collection element";
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