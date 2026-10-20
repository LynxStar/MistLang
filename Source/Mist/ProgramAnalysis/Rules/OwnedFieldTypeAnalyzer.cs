using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class OwnedFieldTypeAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types.OfType<ConcreteType>().Where(type => type is { IsOwned: true, IsIntrinsic: false }))
        {
            foreach(var field in type.FieldsList)
            {
                if(OwnedFieldTypes.WhyRefused(field) is { } reason && (field.DeclarationNode ?? Declarations.NodeOf(type)) is { } at)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.OwnedFieldType, at, [],
                        field.Name, TypeDisplay.Mist(type), TypeDisplay.Mist(field.Type), reason);
                }
            }
        }
    }
}