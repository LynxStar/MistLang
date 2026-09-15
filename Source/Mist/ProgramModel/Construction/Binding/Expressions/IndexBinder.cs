using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
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

        if(Indexability.ArrayElement(receiver.Type) is { } element)
        {
            return new IndexExpression(node, receiver, index, element);
        }

        var indexers = Indexability.Indexers(receiver.Type);

        if(indexers.Count == 0)
        {
            return new IndexExpression(node, receiver, index, ErrorType.Instance);
        }

        var choice = Indexability.Select(indexers, receiver.Type, index, context);

        if(choice.Selected is not { } selected)
        {
            OverloadFailure.Report(
                choice, indexers, [index],
                $"indexer '{TypeDisplay.Mist(receiver.Type)}.this[]'", node, context.Diagnostics);

            return new ErrorExpression(node);
        }

        var type = Indexability.IndexerType(selected, choice.Substitution, context);

        if(selected.Modifiers.IsIntrinsic)
        {
            return new IndexExpression(node, receiver, index, type);
        }

        var interner = context.ProgramModel.TypeInterner;
        var parameterTypes = selected.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner));

        return new IndexExpression(
            node, receiver, ImplicitConversions.AtParameters([index], method: null, parameterTypes, interner)[0], type)
        {
            Indexer = selected,
            ParameterType = parameterTypes is [var slot] && !Indexability.IsIndex(index.Type) ? slot : null,
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