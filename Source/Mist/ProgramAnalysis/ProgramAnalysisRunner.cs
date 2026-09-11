using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramAnalysis;

internal static class ProgramAnalysisRunner
{
    private static readonly IProgramAnalyzer[] Analyzers =
    [
        new ExhaustivenessAnalyzer(),
        new CastAnalyzer(),
        new AssignabilityAnalyzer(),
        new AccessibilityAnalyzer(),
        new DeclarationShapeAnalyzer(),
        new ConstraintAnalyzer(),
        new DefaultabilityAnalyzer(),
        new ConformanceAnalyzer(),
        new PatternAnalyzer(),
        new OperatorAnalyzer(),
        new PositionAnalyzer(),
        new DefiniteAssignmentAnalyzer(),
        new StampabilityAnalyzer(),
    ];

    public static void Run(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var nodes = new List<object>();
        var roots = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);

        foreach(var root in SourceBodies.Of(model))
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                nodes.Add(node);
                roots[node] = root;
            }
        }

        var sourcePaths = SourceTypes.UnitsOf(model).Select(source => source.Unit.SourcePath).ToHashSet(StringComparer.Ordinal);

        foreach(var analyzer in Analyzers)
        {
            analyzer.Analyze(new AnalysisContext(model, nodes, roots, sourcePaths, diagnostics, analyzer.Id));
        }
    }
}