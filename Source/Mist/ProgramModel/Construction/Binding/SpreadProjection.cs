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
    /// <summary>
    /// Looked for on the receiver's own declaration and then on each base in turn, each closed by the
    /// one below it, since a projection is inherited like the aggregate it reads. A base answers only
    /// with an element whose position it can count to: one at or past a spread of a pack still open
    /// there has no width yet.
    /// </summary>
    public static LookupResult? Find(
        ConstructedType? receiver, MistType declaring, string memberName, BuilderContext context)
    {
        var construction = receiver;
        var inherited = false;

        for(var definition = declaring as ConcreteType; definition is not null; inherited = true)
        {
            if(construction is not null && Own(construction, definition, memberName, inherited, context) is { } found)
            {
                return found;
            }

            if(definition.BaseType is not { } baseType)
            {
                return null;
            }

            var closed = TypeSubstitution.ForReceiver(construction).Apply(baseType, context.ProgramModel.TypeInterner);
            construction = closed as ConstructedType;
            definition = (construction?.Definition ?? closed) as ConcreteType;
        }

        return null;
    }

    private static LookupResult? Own(
        ConstructedType receiver, ConcreteType declaring, string memberName, bool inherited, BuilderContext context)
    {
        foreach(var field in declaring.FieldsList)
        {
            if(Element(receiver, field.Name, field.Type, memberName, inherited, context) is { } type)
            {
                return new FieldResult(
                    new Field(memberName, type, field.Ownership, field.Modifiers)
                    {
                        StorageSource = field,
                        StorageElementIndex = int.Parse(memberName.AsSpan(field.Name.Length)) - 1,
                    }, declaring, []);
            }
        }

        foreach(var property in declaring.PropertiesList)
        {
            if(Element(receiver, property.Name, property.Type, memberName, inherited, context) is { } type)
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
        ConstructedType receiver, string stem, IMistType declaredType, string memberName, bool inherited, BuilderContext context)
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

        return closed is TupleType tuple
            && index >= 1
            && index <= tuple.ElementTypes.Count
            && !(inherited && tuple.ElementTypes.Take(index).Any(element => element is SpreadType))
                ? tuple.ElementTypes[index - 1]
                : null;
    }
}