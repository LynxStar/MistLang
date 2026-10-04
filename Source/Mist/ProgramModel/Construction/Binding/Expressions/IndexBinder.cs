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
/// a method by its arguments, and is refused as a call is when none or several could take it. An index
/// into a receiver with no indexer at all keeps answering <see cref="ErrorType"/> silently, where a range
/// that slices nothing is reported: it has no C# to write and would reach emission as a slice of an
/// error type.
/// </summary>
internal static class IndexBinder
{
    internal static Expression Bind(
        IndexSuffixPostfixSuffixNode node, Expression receiver, BindingContext ctx, BuilderContext context)
    {
        var index = ExpressionBinder.BindExpression(node.Index, ctx, context);

        if(Indexability.IsRange(index.Type))
        {
            return new IndexExpression(node, receiver, index, SliceType(receiver.Type, node, context));
        }

        var indexers = Indexability.Indexers(receiver.Type);

        if(indexers.Count == 0)
        {
            return new IndexExpression(node, receiver, index, Indexability.ArrayElement(receiver.Type) ?? ErrorType.Instance);
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