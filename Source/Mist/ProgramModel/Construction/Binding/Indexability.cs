using Mist.ProgramModel.Contracts.Types;
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

    public static bool IsIndexable(IMistType type, BuilderContext context)
    {
        return ElementType(type, context) is not ErrorType;
    }

    public static bool IsSliceable(IMistType type, BuilderContext context)
    {
        return SliceType(type, context) is not ErrorType;
    }

    public static IMistType ElementType(IMistType receiverType, BuilderContext context)
    {
        return ArrayElement(receiverType) ?? IndexerType(receiverType, context);
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

    public static Indexer? Indexer(IMistType receiverType)
    {
        return Declared(receiverType) is { Modifiers.IsIntrinsic: false } user ? user : null;
    }

    private static Indexer? Declared(IMistType receiverType)
    {
        var lookupType = (receiverType as ConstructedType)?.Definition ?? receiverType;

        return lookupType is MemberType { IndexersList.Count: > 0 } memberType ? memberType.IndexersList[0] : null;
    }

    private static IMistType IndexerType(IMistType receiverType, BuilderContext context)
    {
        if(Declared(receiverType) is not { } indexer)
        {
            return ErrorType.Instance;
        }

        var constructed = receiverType as ConstructedType;

        var substituted = TypeSubstitution
            .ForReceiver(constructed)
            .Apply(indexer.Type, context.ProgramModel.TypeInterner);

        return ClrInboundLifting.Close(
            indexer.Modifiers,
            substituted,
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            context.ProgramModel.TypeInterner);
    }
}