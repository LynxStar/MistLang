using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static partial class PatternBinder
{
    private static TuplePattern? BindCorrelatedTuple(
        TuplePositionalPatternPrimaryPatternNode node, List<PatternNode> nodes, UnionType input,
        bool bindingsPermitted, BindingContext ctx, BuilderContext context, bool failed)
    {
        var candidates = input.Constituents.OfType<ITupleType>().Where(tuple => tuple.ElementTypes.Count == nodes.Count).ToList();
        var elements = new List<Pattern>();
        var interner = context.ProgramModel.TypeInterner;

        for(var i = 0; i < nodes.Count; i++)
        {
            var types = candidates.Select(tuple => tuple.ElementTypes[i]).ToList();
            var bases = types.Select(Requirement.Unrefined).Distinct().ToList();
            var flowing = types.Count == 0 ? ErrorType.Instance
                : types.Count == 1 ? types[0] : bases.Count == 1 ? bases[0] : interner.GetOrCreateUnionType(types);

            var previous = ctx.IsErrorPattern;
            ctx.IsErrorPattern = i > 0 && ResultTransfers.Inspection(input) is not null && candidates.Count == 1
                && Requirement.ValuesOf(candidates[0].ElementTypes[0])?.Equals(Admitted.Exactly(0)) is true;
            try
            {
                if(Bind(nodes[i], flowing, bindingsPermitted, ctx, context, failed) is not { } pattern)
                {
                    return null;
                }

                elements.Add(pattern);
                _ = candidates.RemoveAll(tuple => !TupleCorrelation.MayMatch(pattern, tuple.ElementTypes[i]));
            }
            finally
            {
                ctx.IsErrorPattern = previous;
            }
        }

        var selected = candidates.Count == 1 ? (IMistType)candidates[0] : null;
        return new TuplePattern(node, elements, selected ?? input, testedType: null, binding: null)
        {
            CorrelatedInput = input,
            Constituent = selected,
            IsFailed = failed || elements.Any(element => element.IsFailed),
        };
    }
}