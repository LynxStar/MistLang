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
/// a method by its arguments, and is refused as a call is when none or several could take it. A range
/// into a countable user type selects its Range indexer the same way and, failing one, reaches its
/// Slice(int, int) as C#'s implicit range support does, which is what lets the emitted access stay
/// <c>receiver[range]</c>.
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

        var isRange = Indexability.IsRange(index.Type);

        if(isRange && Indexability.SliceType(receiver.Type, context) is not ErrorType and var sliced)
        {
            return new IndexExpression(node, receiver, index, sliced);
        }

        var indexers = Indexability.Indexers(receiver.Type);
        var choice = indexers.Count == 0 ? null : Indexability.Select(indexers, receiver.Type, index, context);
        var indexerTakesRange = choice is { Ambiguous.Count: > 0 }
            || (Indexability.Takes(choice, index.Type, context) && Indexability.Count(receiver.Type) is not null);
        var access = ctx.AccessAt(node);

        if(isRange && !indexerTakesRange)
        {
            return BindSlice(node, receiver, index, ctx, context);
        }

        if(choice is null)
        {
            if(Indexability.ArrayElement(receiver.Type) is { } element)
            {
                if(access.HasFlag(StorageAccess.Write) && UnknownViews.HiddenElement(receiver.Type) is { } hiddenElement)
                {
                    UnknownViews.Report(hiddenElement, receiver.Type, node, context);
                    return new ErrorExpression(node, [receiver, index]);
                }

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

        if(choice.Selected is not { } selected)
        {
            return OverloadFailure.Retain(
                choice, indexers, [index],
                $"indexer '{TypeDisplay.Mist(receiver.Type)}.this[]'", node, context.Diagnostics, [receiver, index]);
        }

        if(UnknownViews.IsClosedWithUnknown(receiver.Type) && UnknownViews.Hidden(selected, access, choice.Substitution, context) is { } hidden)
        {
            UnknownViews.Report(hidden, receiver.Type, node, context);
            return new ErrorExpression(node, [receiver, index]);
        }

        var type = Indexability.ArrayElement(receiver.Type) ?? Indexability.IndexerType(selected, choice.Substitution, context);
        var interner = context.ProgramModel.TypeInterner;
        var parameterTypes = selected.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner));
        CountedIndexer? fromEnd = null;
        var readsPosition = !selected.Modifiers.IsIntrinsic && Indexability.IsIndex(index.Type) && parameterTypes is [_] && !Indexability.IsIndex(parameterTypes[0]);
        var takesPosition = readsPosition && Indexability.Takes(choice, context.Resolver.LookupWellKnown(WellKnownTypes.Int32), context);

        if(takesPosition && parameterTypes is [var position])
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
            ParameterType = parameterTypes is [var slot] && (!Indexability.IsIndex(index.Type) || (readsPosition && !takesPosition)) ? slot : null,
            FromEnd = fromEnd,
        };
    }

    private static void ReportNotIndexable(IMistType receiverType, IndexSuffixPostfixSuffixNode node, BuilderContext context)
    {
        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.ReceiverNotIndexable, node, null, TypeDisplay.Mist(receiverType));
    }

    private static Expression BindSlice(
        IndexSuffixPostfixSuffixNode node, Expression receiver, Expression index, BindingContext ctx, BuilderContext context)
    {
        if(Indexability.Count(receiver.Type) is { } count
            && Indexability.SelectSlice(receiver.Type, node, context) is { Selected: { } slice } choice)
        {
            context.ProgramModel.TaskDrops.RecordCall(ctx.EnclosingMethod, slice);

            return new IndexExpression(node, receiver, index, Indexability.SliceResult(slice, choice.Substitution, context))
            {
                Slice = new CountedSlice(count, null, slice),
            };
        }

        if(FailureDependency.NeedsAnalysis(receiver) || FailureDependency.NeedsAnalysis(index))
        {
            var pending = context.Diagnostics.Withhold(() => ReportNotSliceable(receiver.Type, node, context));
            return new ErrorExpression(node, [receiver, index], pending.Single());
        }

        ReportNotSliceable(receiver.Type, node, context);
        return new ErrorExpression(node, [receiver, index]);
    }

    private static void ReportNotSliceable(IMistType receiverType, IndexSuffixPostfixSuffixNode node, BuilderContext context)
    {
        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.ReceiverNotSliceable, node, null, TypeDisplay.Mist(receiverType));
    }
}