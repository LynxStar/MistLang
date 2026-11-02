using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class ResultAnalyzer : IProgramAnalyzer
{
    public string Id => "result-contracts";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is ResultPropagationExpression propagation
                && JudgePropagation(propagation.Result.Error, propagation.FailureReturn.TargetType, context.Model.TypeInterner) is { } refusal)
            {
                context.Report(refusal, propagation.Node, [propagation, propagation.ErrorValue, propagation.FailureReturn.Value!]);
            }
            else if(node is TuplePattern { CorrelatedInput: not null, Constituent: null } tuple)
            {
                context.Report(ProgramAnalysisDiagnostics.TupleCorrelationUnproven, tuple.Node, []);
            }
        }

        foreach(var root in context.Bodies)
        {
            if(BodyWalker.DescendantsAndSelf(root.Root).OfType<ResultIdentityExpression>().Any())
            {
                ResultErrorFlow.Judge(root.Root, context);
            }
        }
    }

    public static MistDiagnostic? JudgePropagation(IMistType error, IMistType? returnType, TypeInterner interner)
    {
        if(returnType is null || OptionalUnion.IsVoid(returnType))
        {
            return ProgramAnalysisDiagnostics.ResultPropagationDestination;
        }

        var destination = ResultTypes.Of(returnType)?.Error ?? returnType;
        return ResultTypes.Fits(error, destination, interner) ? null : ProgramAnalysisDiagnostics.ResultPropagationError;
    }
}