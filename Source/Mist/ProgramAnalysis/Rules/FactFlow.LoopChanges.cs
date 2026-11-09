using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class FactFlow
{
    private sealed class LoopChanges(IReadOnlyList<Expression> nodes)
    {
        public static Dictionary<FlowBlock, LoopChanges> Of(FlowGraph? graph)
        {
            var result = new Dictionary<FlowBlock, LoopChanges>();
            var evaluated = graph?.Blocks.SelectMany(block => block.Steps).Where(step => step.Kind == FlowStepKind.Evaluate)
                .Select(step => step.Node).ToHashSet(ReferenceEqualityComparer.Instance) ?? [];
            foreach(var loop in graph?.Loops ?? [])
            {
                var nodes = BodyWalker.DescendantsAndSelf(loop.Body).OfType<Expression>().Where(evaluated.Contains)
                    .Concat(loop.Latch.Steps.Select(step => step.Node).OfType<Expression>()).ToList();
                result.Add(loop.Header, new LoopChanges(nodes));
            }

            return result;
        }

        public bool Writes(object holder, FactState incoming, FactReading reading, FieldEffects effects)
        {
            foreach(var expression in nodes)
            {
                var target = expression switch
                {
                    AssignmentExpression assignment => assignment.Target,
                    CompoundAssignmentExpression compound => compound.Target,
                    _ => null,
                };
                if(target is not null && Equals(reading.Holder(ref incoming, target), holder))
                {
                    return true;
                }

                if(holder is Field field && (target is ThisExpression || (!field.Modifiers.IsReadonly && effects.MayWrite(expression))))
                {
                    return true;
                }

                var (arguments, parameters) = expression switch
                {
                    InvocationExpression call => (call.Arguments, call.Parameters),
                    NewExpression creation => (creation.Arguments, creation.Constructor?.ParametersList),
                    _ => (null, null),
                };
                for(var i = 0; arguments is not null && parameters is not null && i < arguments.Count && i < parameters.Count; i++)
                {
                    if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out
                        && Equals(reading.Holder(ref incoming, arguments[i]), holder))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}