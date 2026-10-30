using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class OwnedFieldTypeAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var derived = new OwnedProvenance.Derivations();

        foreach(var type in context.Types.OfType<ConcreteType>().Where(type => type is { IsOwned: true, IsIntrinsic: false }))
        {
            foreach(var violation in OwnedFieldTypes.Of(type, derived))
            {
                if(((violation.InheritedFrom is null ? violation.Slot.DeclarationNode : null) ?? Declarations.NodeOf(type)) is { } at)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.OwnedFieldType, at, [],
                        new EnumValue(violation.Slot.Property is null ? "Field" : "Property"),
                        new NameValue(violation.InheritedFrom is { } inheritedFrom ? $"{TypeDisplay.Mist(inheritedFrom)}.{violation.Slot.Name}" : violation.Slot.Name),
                        DiagnosticSnapshot.Type(type), DiagnosticSnapshot.Type(violation.Type), new NameValue(violation.Reason));
                }
            }
        }
    }
}