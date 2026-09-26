using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class StampabilityAnalyzer : IProgramAnalyzer
{
    public string Id => "flow-framework";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types.OfType<StructType>())
        {
            if(type.Modifiers.IsStampable
                && !type.IsRef
                && type.DeclarationNode is { } declaration
                && Stampability.WhyNotStampable(type) is { } reason)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.StampableDeclarationNotSatisfied, declaration, [],
                    TypeDisplay.Mist(type), reason);
            }
        }

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case ArrayCreationExpression array when WhyNot(array.ElementType) is { } reason:
                    context.Report(
                        ProgramAnalysisDiagnostics.ArrayRequiresInitializer, array.Node, [array],
                        TypeDisplay.Mist(array.ElementType), reason);
                    break;

                case StackallocExpression allocation when WhyNot(allocation.ElementType) is { } reason:
                    context.Report(
                        ProgramAnalysisDiagnostics.StackallocElementNotStampable, allocation.Node, [allocation],
                        TypeDisplay.Mist(allocation.ElementType), reason);
                    break;
            }
        }
    }

    private static string? WhyNot(IMistType elementType)
    {
        return elementType is ErrorType ? null : Stampability.WhyNotStampable(elementType);
    }
}