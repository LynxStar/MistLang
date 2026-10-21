using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static partial class PatternBinder
{
    /// <summary>
    /// The positional form of variadics Requirement 12.9's type test: <c>args is (int a, int b)</c> over
    /// <c>(TArgs...)</c> asks what <c>args is (int, int) pair</c> asks, element by element. The width is
    /// open here, so no width is a mismatch to refuse -- each specialization answers it -- and no element
    /// test is one either, the pack's elements being as open as any type parameter.
    /// </summary>
    private static TuplePattern? BindOverOpenPack(
        TuplePositionalPatternPrimaryPatternNode node,
        List<PatternNode> elementNodes,
        TypeParameter pack,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context, bool scrutineeFailed)
    {
        var elementTypes = new List<IMistType>(elementNodes.Count);
        var elements = new List<Pattern>(elementNodes.Count);

        for(var i = 0; i < elementNodes.Count; i++)
        {
            elementTypes.Add(pack.ElementAt(i));

            if(Bind(elementNodes[i], elementTypes[i], bindingsPermitted, ctx, context, scrutineeFailed) is not { } bound)
            {
                return null;
            }

            elements.Add(bound);
        }

        var tested = context.ProgramModel.TypeInterner.GetOrCreateTupleType(elementTypes);

        return new TuplePattern(node, elements, tested, tested, binding: null)
        {
            OpenPack = pack,
            IsFailed = scrutineeFailed || elements.Any(element => element.IsFailed),
        };
    }

    private static TypeParameter? NarrowedPack(IMistType scrutineeType, IMistType tested)
    {
        return scrutineeType is TypeParameter { IsPack: true } pack && PackTypePositions.ProvenLength(tested) is not null ? pack : null;
    }
}