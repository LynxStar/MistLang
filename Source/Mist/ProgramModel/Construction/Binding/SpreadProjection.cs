using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The per-element members a spread declaration stands for: <c>public TArgs... Item;</c> names the
/// aggregate <c>Item</c> and one projection <c>Item1</c> .. <c>ItemN</c>.
/// <para>
/// FOUND ON DEMAND RATHER THAN DECLARED, because N is not a property of the declaration. The same
/// <c>Bundle</c> has three projections where its pack holds three and none where it holds none, so
/// the family exists per CONSTRUCTION and the receiver is the only thing that knows which.
/// </para>
/// </summary>
internal static class SpreadProjection
{
    public static LookupResult? Find(
        ConstructedType? receiver, MistType declaring, string memberName, BuilderContext context)
    {
        if(receiver is null || declaring is not ConcreteType concrete)
        {
            return null;
        }

        foreach(var field in concrete.FieldsList)
        {
            if(Element(receiver, field.Name, field.Type, memberName, context) is { } type)
            {
                return new FieldResult(
                    new Field(memberName, type, field.Ownership, field.Modifiers), declaring, []);
            }
        }

        foreach(var property in concrete.PropertiesList)
        {
            if(Element(receiver, property.Name, property.Type, memberName, context) is { } type)
            {
                return new PropertyResult(
                    new Property(memberName, type, property.Modifiers)
                    {
                        DeclaringType = property.DeclaringType,
                        Getter = property.Getter,
                        Setter = property.Setter,
                    },
                    declaring, []);
            }
        }

        return null;
    }

    /// <summary>
    /// The element a projection name denotes, or null when the name is not one of this member's.
    /// An index outside the pack is not one of them either: <c>Item4</c> on a pack of three names
    /// nothing, and saying so lets the ordinary "no such member" answer stand.
    /// </summary>
    private static IMistType? Element(
        ConstructedType receiver, string stem, IMistType declaredType, string memberName, BuilderContext context)
    {
        if(!SpreadMember.IsSpread(declaredType)
            || memberName.Length <= stem.Length
            || !memberName.StartsWith(stem, StringComparison.Ordinal)
            || !int.TryParse(memberName.AsSpan(stem.Length), out var index))
        {
            return null;
        }

        var closed = TypeSubstitution.ForReceiver(receiver)
            .Apply(declaredType, context.ProgramModel.TypeInterner);

        return closed is TupleType tuple && index >= 1 && index <= tuple.ElementTypes.Count
            ? tuple.ElementTypes[index - 1]
            : null;
    }
}