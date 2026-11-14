using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class UnknownClosureAnalyzer : IProgramAnalyzer
{
    public string Id => "unknown-closure-construction";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case NewExpression creation when Judge(creation.Type) is { } finding:
                    context.Report(finding, creation.Node, [creation], DiagnosticSnapshot.Type(creation.Type), UnknownPositions(creation.Type));
                    break;

                case DefaultExpression { Type: UnknownType } defaulted:
                    context.Report(ProgramAnalysisDiagnostics.UnknownHasNoDefault, defaulted.Node, [defaulted]);
                    break;
            }
        }
    }

    internal static MistDiagnostic? Judge(IMistType constructed)
    {
        return UnknownViews.IsClosedWithUnknown(constructed) ? ProgramAnalysisDiagnostics.UnknownClosureConstructed : null;
    }

    internal static SequenceValue UnknownPositions(IMistType constructed)
    {
        var names = new List<SlotValue>();

        for(var current = Ancestry.Unwrap(constructed) as ConstructedType; current is not null; current = current.Enclosing)
        {
            if(current.Definition is not IHasTypeParameters definition)
            {
                continue;
            }

            foreach(var parameter in definition.TypeParametersList)
            {
                if(!parameter.IsDependent
                    && parameter.Ordinal < current.TypeArguments.Count
                    && TypeParameterOccurrences.BearsUnknown(current.TypeArguments[parameter.Ordinal]))
                {
                    names.Add(new NameValue(parameter.Name));
                }
            }
        }

        return new SequenceValue(SlotKind.Name, [.. names], Separator: " and ", ItemPrefix: "'", ItemSuffix: "'");
    }
}