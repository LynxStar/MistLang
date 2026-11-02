using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class OwnershipFlow
{
    private (Expression Source, TuplePattern Pattern, Place Place)? SnapshotAt(FlowStep step)
    {
        if(step.Kind != FlowStepKind.Bind || step.Variable is null)
        {
            return null;
        }

        var owner = PayloadPattern(step.Node);
        if(owner is null)
        {
            return null;
        }

        object current = owner;
        while(parents.TryGetValue(current, out var parent))
        {
            var source = parent switch
            {
                IsPatternExpression test => test.Scrutinee,
                SwitchArm arm when parents.TryGetValue(arm, out var enclosing) && enclosing is SwitchExpression choice => choice.Scrutinee,
                _ => null,
            };

            if(source is not null && ResultTransfers.Inspection(source.Type) is not null
                && Place.Of(source) is { } place && Place.Tracked(place))
            {
                return (source, owner, place);
            }

            current = parent;
        }

        return null;
    }

    private TuplePattern? PayloadPattern(object node)
    {
        while(parents.TryGetValue(node, out var parent))
        {
            if(parent is TuplePattern { CorrelatedInput: not null, Elements.Count: > 1 } tuple && !ReferenceEquals(node, tuple.Elements[0]))
            {
                return tuple;
            }

            node = parent;
        }

        return null;
    }
}