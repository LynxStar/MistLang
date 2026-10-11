using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds <c>a[x]</c>, which yields an element or a sub-sequence depending on what <c>x</c> IS, so the
/// receiver alone cannot type it -- <c>numbers[1]</c> and <c>numbers[1..3]</c> differ in result type.
/// An element access into a receiver that declares indexers selects one by the index, as a call selects
/// a method by its arguments, and is refused as a call is when none or several could take it.
/// </summary>
internal static class IndexBinder
{
    internal static Expression Bind(
        IndexSuffixPostfixSuffixNode node, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        var index = ExpressionBinder.BindExpression(node.Index, ctx, context);

        if(PriorFailure.Of(receiver) || PriorFailure.Of(index))
        {
            return new ErrorExpression(node, [receiver, index]);
        }

        if(Indexability.IsRange(index.Type))
        {
            return new IndexExpression(node, receiver, index, SliceType(receiver.Type, node, context));
        }

        var indexers = Indexability.Indexers(receiver.Type);

        if(indexers.Count == 0)
        {
            if(Indexability.ArrayElement(receiver.Type) is { } element)
            {
                return new IndexExpression(node, receiver, index, element);
            }

            if(!Indexability.RefusesElement(receiver.Type, indexers))
            {
                return new IndexExpression(node, receiver, index, ErrorType.Instance);
            }

            if(FailureDependency.NeedsAnalysis(receiver) || FailureDependency.NeedsAnalysis(index))
            {
                var pending = context.Diagnostics.Withhold(() => ReportNotIndexable(receiver.Type, node, context));
                return new ErrorExpression(node, [receiver, index], pending.Single());
            }

            ReportNotIndexable(receiver.Type, node, context);
            return new ErrorExpression(node, [receiver, index]);
        }

        var choice = Indexability.Select(indexers, receiver.Type, index, context);

        if(choice.Selected is not { } selected)
        {
            return OverloadFailure.Retain(
                choice, indexers, [index],
                $"indexer '{TypeDisplay.Mist(receiver.Type)}.this[]'", node, context.Diagnostics, [receiver, index]);
        }

        var type = Indexability.ArrayElement(receiver.Type) ?? Indexability.IndexerType(selected, choice.Substitution, context);
        var interner = context.ProgramModel.TypeInterner;
        var parameterTypes = selected.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner));
        CountedIndexer? fromEnd = null;

        if(!selected.Modifiers.IsIntrinsic && Indexability.IsIndex(index.Type) && parameterTypes is [var position] && !Indexability.IsIndex(position))
        {
            if(Indexability.Count(receiver.Type) is { } count)
            {
                fromEnd = new CountedIndexer(selected, position, count);
            }
            else
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.ReceiverNotCountable, node, null, TypeDisplay.Mist(receiver.Type));
            }
        }

        return new IndexExpression(
            node, receiver, ImplicitConversions.AtParameters([index], method: null, parameterTypes, interner)[0], type)
        {
            Indexer = selected.Modifiers.IsIntrinsic ? null : selected,
            ParameterType = parameterTypes is [var slot] && !Indexability.IsIndex(index.Type) ? slot : null,
            FromEnd = fromEnd,
        };
    }

    private static void ReportNotIndexable(IMistType receiverType, IndexSuffixPostfixSuffixNode node, BuilderContext context)
    {
        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.ReceiverNotIndexable, node, null, TypeDisplay.Mist(receiverType));
    }

    private static IMistType SliceType(IMistType receiverType, IndexSuffixPostfixSuffixNode node, BuilderContext context)
    {
        var sliced = Indexability.SliceType(receiverType, context);

        if(sliced is ErrorType)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.ReceiverNotSliceable, node, null, receiverType.Name);
        }

        return sliced;
    }
}