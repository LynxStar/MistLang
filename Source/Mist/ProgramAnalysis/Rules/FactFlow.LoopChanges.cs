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
            if(graph is null || graph.Loops.Count == 0)
            {
                return result;
            }

            var predecessors = graph.Blocks.Select(_ => new List<FlowBlock>()).ToArray();
            foreach(var block in graph.Blocks)
            {
                foreach(var successor in block.Successors)
                {
                    predecessors[successor.Ordinal].Add(block);
                }
            }

            foreach(var loop in graph.Loops)
            {
                // Only paths back to the header can change the next round; nested function bodies belong to separate graphs.
                var region = new HashSet<FlowBlock> { loop.Header };
                var pending = new Stack<FlowBlock>();
                pending.Push(loop.Latch);
                while(pending.TryPop(out var block))
                {
                    if(!region.Add(block))
                    {
                        continue;
                    }

                    foreach(var predecessor in predecessors[block.Ordinal])
                    {
                        pending.Push(predecessor);
                    }
                }

                var nodes = graph.Blocks.Where(region.Contains).SelectMany(block => block.Steps)
                    .Where(step => step.Kind == FlowStepKind.Evaluate).Select(step => step.Node).OfType<Expression>().ToList();
                result.Add(loop.Header, new LoopChanges(nodes));
            }

            return result;
        }

        public bool Writes(object holder, FactState incoming, FactFlow flow)
        {
            foreach(var expression in nodes)
            {
                var target = expression switch
                {
                    AssignmentExpression assignment => assignment.Target,
                    CompoundAssignmentExpression compound => compound.Target,
                    _ => null,
                };
                if(target is not null && Ends(flow, ref incoming, target, holder))
                {
                    return true;
                }

                if(holder is Field field && (target is ThisExpression || (!field.Modifiers.IsReadonly && flow.effects.MayWrite(expression))))
                {
                    return true;
                }

                var (arguments, parameters) = BodyHolders.Arguments(expression);
                for(var i = 0; arguments is not null && parameters is not null && i < arguments.Count && i < parameters.Count; i++)
                {
                    if(parameters[i].Modifier.Direction is ByRefDirection.Ref or ByRefDirection.Out && Ends(flow, ref incoming, arguments[i], holder))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Ends(FactFlow flow, ref FactState incoming, Expression place, object holder)
        {
            if(BodyHolders.ReturnsRef(place))
            {
                foreach(var named in BodyHolders.Places(place))
                {
                    if(Ends(flow, ref incoming, named, holder))
                    {
                        return true;
                    }
                }

                return false;
            }

            return flow.reading.Holder(ref incoming, place) is { } written
                && (Equals(written, holder) || flow.holders.AliasesOf(written).Contains(holder));
        }
    }
}