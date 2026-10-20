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
            foreach(var violation in OwnedFieldTypes.Of(type))
            {
                if(((violation.InheritedFrom is null ? violation.Slot.DeclarationNode : null) ?? Declarations.NodeOf(type)) is { } at)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.OwnedFieldType, at, [],
                        violation.Slot.Property is null ? "Field" : "Property",
                        violation.InheritedFrom is { } inheritedFrom ? $"{TypeDisplay.Mist(inheritedFrom)}.{violation.Slot.Name}" : violation.Slot.Name,
                        TypeDisplay.Mist(type), TypeDisplay.Mist(violation.Type), violation.Reason);
                }
            }
        }
    }
}