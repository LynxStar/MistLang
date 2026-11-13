using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class RefinementAnalyzer
{
    private sealed partial class Proving
    {
        private void NeedClosing(FactState state, Expression value, IMistType target)
        {
            if(value.Type is not ErrorType && SlotIdentity.Judge(state, value, target, factReading, budget) is { } unproven)
            {
                ReportClosing(unproven, value.Node, [value]);
            }
        }

        private void NeedClosing(FactState state, IMistType source, IMistType target, Storm.Runtime.SyntaxNode at, IReadOnlyList<Expression> about)
        {
            if(source is not ErrorType && SlotIdentity.Judge(state, source, target, through: null, factReading, budget) is { } unproven)
            {
                ReportClosing(unproven, at, about);
            }
        }

        private void ReportClosing(UnprovenSlot unproven, Storm.Runtime.SyntaxNode at, IReadOnlyList<Expression> about)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ClosingNotProven, at, about,
                DiagnosticSnapshot.Type(unproven.Source), DiagnosticSnapshot.Type(unproven.Target),
                new NameValue(unproven.Left), new NameValue(unproven.Right), RefinementFindings.Case(unproven.Proof));
        }

        private static IEnumerable<(IMistType Element, LocalVariable Target)> Deconstructed(TupleDeconstructionStatement deconstruction)
        {
            var elements = deconstruction.Initializer.Type switch
            {
                INamedTupleType named => named.Underlying.ElementTypes,
                ITupleType tuple => tuple.ElementTypes,
                _ => null,
            };

            if(elements is null || elements.Count != deconstruction.Targets.Count)
            {
                yield break;
            }

            for(var i = 0; i < elements.Count; i++)
            {
                if(deconstruction.ElementConversions[i] is null)
                {
                    yield return (elements[i], deconstruction.Targets[i]);
                }
            }
        }
    }
}