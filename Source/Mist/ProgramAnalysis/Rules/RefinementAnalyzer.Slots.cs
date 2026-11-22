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
            if(value.Type is not ErrorType && SlotIdentity.Judge(state, value, target, factReading, budget, Met(null)) is { } unproven)
            {
                ReportClosing(unproven, value.Node, [value]);
            }
        }

        private void NeedClosing(FactState state, IMistType source, IMistType target, Storm.Runtime.SyntaxNode at, IReadOnlyList<Expression> about, SlotMeeting? meeting = null)
        {
            if(source is not ErrorType && SlotIdentity.Judge(state, source, target, through: null, factReading, budget, Met(meeting)) is { } unproven)
            {
                ReportClosing(unproven, at, about);
            }
        }

        private void NeedSignature(FactState state, FunctionReferenceExpression reference, IFunctionType target)
        {
            var taking = new SlotMeeting(reference.Captures, Receiver: reference.Receiver) { Inner = SlotMeeting.Inside(reference.Captures, reference.TypeArguments) };
            var pairs = target.ParameterTypes.Zip(reference.ClosedParameterTypes).Select(pair => (Source: pair.First, Target: pair.Second, Meeting: taking));

            if(reference.ClosedReturnType is { } returned)
            {
                pairs = pairs.Append((returned, target.ReturnType, SlotMeeting.Fit));
            }

            foreach(var (source, met, meeting) in pairs)
            {
                if(source is not ErrorType && SlotIdentity.Judge(state, source, met, through: null, factReading, budget, Met(meeting)) is { } unproven)
                {
                    ReportClosing(unproven, reference.Node, [reference]);
                    return;
                }
            }
        }

        private SlotMeeting Met(SlotMeeting? meeting)
        {
            return (meeting ?? SlotMeeting.Fit) with { Values = sites.Values };
        }

        private void ReportClosing(UnprovenSlot unproven, Storm.Runtime.SyntaxNode at, IReadOnlyList<Expression> about)
        {
            context.Report(
                unproven.Refinement is null ? ProgramAnalysisDiagnostics.ClosingNotProven : ProgramAnalysisDiagnostics.ClosingRefinementNotProven, at, about,
                DiagnosticSnapshot.Type(unproven.Source), DiagnosticSnapshot.Type(unproven.Target),
                new NameValue(unproven.Left), new NameValue(unproven.Refinement ?? unproven.Right), RefinementFindings.Case(unproven.Proof));
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