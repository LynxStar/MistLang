using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether a receiver can be indexed or sliced, and what either yields. The ARGUMENT's type asks the
/// question -- an index reads one element out, a range reads a contiguous sub-sequence -- so an element
/// access cannot be typed from its receiver alone, and a list pattern needs both answers at once for
/// its positional elements and its slice.
/// </summary>
internal static class Indexability
{
    public static bool IsRange(IMistType type)
    {
        return type.FullyQualifiedName == WellKnownTypes.Range.FullyQualifiedName;
    }

    public static bool IsIndex(IMistType type)
    {
        return type.FullyQualifiedName == WellKnownTypes.Index.FullyQualifiedName;
    }

    public static IMistType? ArrayElement(IMistType type)
    {
        return type is ConstructedType constructed
            && constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName
            ? constructed.TypeArguments[0]
            : null
            ;
    }

    public static bool IsSliceable(IMistType type, BuilderContext context)
    {
        return SliceType(type, context) is not ErrorType;
    }

    public static IMistType ElementType(IMistType receiverType, Expression index, BuilderContext context)
    {
        if(ArrayElement(receiverType) is { } element)
        {
            return element;
        }

        return Indexers(receiverType) is { Count: > 0 } indexers
            && Select(indexers, receiverType, index, context) is { Selected: { } selected } choice
            ? IndexerType(selected, choice.Substitution, context)
            : ErrorType.Instance
            ;
    }

    public static IMistType SliceType(IMistType receiverType, BuilderContext context)
    {
        if(ArrayElement(receiverType) is { } element)
        {
            return context.Resolver.ConstructArray(element, null);
        }

        if(receiverType is ConstructedType { TypeArguments.Count: 2 } spanLike && SpanKind(spanLike.Definition) is { } kind)
        {
            return context.Resolver.ConstructLengthIndexed(kind, spanLike.TypeArguments[0], null);
        }

        return receiverType.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName
            ? receiverType
            : ErrorType.Instance
            ;
    }

    private static WellKnownType? SpanKind(IMistType definition)
    {
        return definition.FullyQualifiedName == WellKnownTypes.Span.FullyQualifiedName ? WellKnownTypes.Span
            : definition.FullyQualifiedName == WellKnownTypes.ReadOnlySpan.FullyQualifiedName ? WellKnownTypes.ReadOnlySpan
            : null;
    }

    public static IReadOnlyList<Indexer> Indexers(IMistType receiverType)
    {
        var lookupType = (receiverType as ConstructedType)?.Definition ?? receiverType;

        return lookupType is MemberType memberType ? memberType.IndexersList : [];
    }

    /// <summary>
    /// A from-end index that no indexer takes as it is reaches the element through a position instead,
    /// as C# lowers <c>c[^1]</c> onto Count and an int indexer, so it selects as a list pattern's
    /// positional element does.
    /// </summary>
    public static OverloadChoice<Indexer> Select(
        IReadOnlyList<Indexer> indexers, IMistType receiverType, Expression index, BuilderContext context)
    {
        var constructed = receiverType as ConstructedType;
        var interner = context.ProgramModel.TypeInterner;
        var choice = OverloadResolution.Select(indexers, [index], constructed, null, interner);

        return choice.Selected is null && IsIndex(index.Type)
            ? OverloadResolution.Select(indexers, [Position(index.Node, context)], constructed, null, interner)
            : choice
            ;
    }

    public static Expression Position(Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        return new DefaultExpression(node, context.Resolver.LookupWellKnown(WellKnownTypes.Int32));
    }

    public static IMistType IndexerType(Indexer indexer, TypeSubstitution substitution, BuilderContext context)
    {
        return ClrInboundLifting.Close(
            indexer.Modifiers,
            substitution.Apply(indexer.Type, context.ProgramModel.TypeInterner),
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            context.ProgramModel.TypeInterner);
    }
}