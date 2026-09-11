using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramAnalysis;

internal static class ProgramAnalysisRunner
{
    private static readonly IProgramAnalyzer[] Analyzers =
        [new ExhaustivenessAnalyzer(), new CastAnalyzer(), new AssignabilityAnalyzer()];

    public static void Run(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var nodes = SourceBodies.Of(model).SelectMany(BodyWalker.DescendantsAndSelf).ToList();

        foreach(var analyzer in Analyzers)
        {
            analyzer.Analyze(new AnalysisContext(model, nodes, diagnostics, analyzer.Id));
        }
    }
}