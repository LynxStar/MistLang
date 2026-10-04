using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class FailedExpressionAnalyzer : IProgramAnalyzer
{
    public string Id => "failed-expression-fallbacks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var expression in context.Nodes.OfType<Expression>())
        {
            context.ResolveFailure(expression);
        }
    }
}