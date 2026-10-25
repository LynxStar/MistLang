using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The two values a program cannot make of <c>unknown</c> (unknown-type Requirements 5.6, 5.7 and
/// 10.5). A generic closed with <c>unknown</c> is a VIEW of some concrete instantiation, widened into
/// it; constructing one would create storage typed by the top type, which the storage ban refuses
/// everywhere else, so <c>new</c> is refused whatever constructor it names. And <c>unknown</c> has no
/// default: no value is unknown and nothing more, so a slot defaulted to it would hold nothing at all.
/// </summary>
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
                    context.Report(finding, creation.Node, [creation], TypeDisplay.Mist(creation.Type), UnknownPositions(creation.Type));
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

    /// <summary>
    /// The type parameters the construction binds to <c>unknown</c>, named for the diagnostic, which
    /// has to say WHERE the top type sits in a long argument list.
    /// </summary>
    internal static string UnknownPositions(IMistType constructed)
    {
        var names = new List<string>();

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
                    names.Add($"'{parameter.Name}'");
                }
            }
        }

        return string.Join(" and ", names);
    }
}