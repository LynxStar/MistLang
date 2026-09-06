using Mist.ProgramModel.Contracts.Types;
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

        return receiverType.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName
            ? receiverType
            : ErrorType.Instance
            ;
    }

    private static IMistType IndexerType(IMistType receiverType, BuilderContext context)
    {
        var constructed = receiverType as ConstructedType;
        var lookupType = constructed?.Definition ?? receiverType;

        if(lookupType is not MemberType { IndexersList.Count: > 0 } memberType)
        {
            return ErrorType.Instance;
        }

        var indexer = memberType.IndexersList[0];

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