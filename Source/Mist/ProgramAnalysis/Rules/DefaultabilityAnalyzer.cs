using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A <c>default</c> of a class or struct the synthesis cannot build an instance of. Judged here, against
/// the Mist source, where before the backend declined to emit it and named the expression unsupported.
/// Order 6 of the analysis register; <see cref="Defaultability"/> is the decision.
/// </summary>
internal sealed class DefaultabilityAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is DefaultExpression { Type: ConcreteType { IsIntrinsic: false } type } defaulted
                && !Defaultability.IsDefaultable(type))
            {
                context.Report(ProgramAnalysisDiagnostics.NotDefaultable, defaulted.Node, [defaulted], TypeDisplay.Mist(type));
            }
        }
    }
}