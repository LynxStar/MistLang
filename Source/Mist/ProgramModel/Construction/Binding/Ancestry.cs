using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a receiver binds the type parameters of a member's declaring type to, where the bound tree kept
/// the member but not the constructed bases it was reached through. A call records its callee and the
/// receiver expression; the path from the receiver's definition to the declaration is walked again
/// here, by the rules <see cref="TypeMemberLookup"/> finds members by, so that a member of
/// <c>Base&lt;X&gt;</c> reached on a <c>Derived&lt;int&gt; : Base&lt;List&lt;Y&gt;&gt;</c> reads X as
/// <c>List&lt;int&gt;</c>.
/// </summary>
internal static class Ancestry
{
    /// <summary>
    /// Null where <paramref name="receiverType"/> neither is nor derives from <paramref name="declaring"/>,
    /// nor sits inside it.
    /// </summary>
    public static TypeSubstitution? SubstitutionTo(IMistType receiverType, MemberType declaring, TypeInterner? interner)
    {
        var receiver = Unwrap(receiverType);
        var constructed = receiver as ConstructedType;
        var definition = constructed?.Definition ?? receiver;

        if(definition is not MistType start)
        {
            return null;
        }

        if(ReferenceEquals(start, declaring))
        {
            return TypeSubstitution.ForReceiver(constructed);
        }

        if(PathTo(start, declaring) is { } path)
        {
            return TypeSubstitution.ForPath(constructed, path, interner);
        }

        for(var enclosing = constructed?.Enclosing; enclosing is not null; enclosing = enclosing.Enclosing)
        {
            if(ReferenceEquals(enclosing.Definition, declaring))
            {
                return TypeSubstitution.ForReceiver(enclosing);
            }
        }

        for(var outer = start.DeclaringType; outer is not null; outer = outer.DeclaringType)
        {
            if(ReferenceEquals(outer, declaring))
            {
                return TypeSubstitution.Empty;
            }
        }

        return null;
    }

    /// <summary>
    /// The constructed bases stepped through from <paramref name="from"/> to <paramref name="declaring"/>,
    /// outermost first, or null where the walk never reaches it.
    /// </summary>
    public static IReadOnlyList<ConstructedType>? PathTo(MistType from, MemberType declaring)
    {
        var queue = new Queue<(MistType Type, IReadOnlyList<ConstructedType> Path)>();
        var seen = new HashSet<MistType>(ReferenceEqualityComparer.Instance) { from };
        queue.Enqueue((from, []));

        while(queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();

            if(ReferenceEquals(current, declaring))
            {
                return path;
            }

            foreach(var baseType in BasesOf(current))
            {
                var constructed = baseType as ConstructedType;

                if((constructed?.Definition ?? baseType) is MistType definition && seen.Add(definition))
                {
                    queue.Enqueue((definition, constructed is null ? path : [.. path, constructed]));
                }
            }
        }

        return null;
    }

    private static IEnumerable<IMistType> BasesOf(MistType type)
    {
        if(type is ConcreteType { BaseType: { } concreteBase })
        {
            yield return concreteBase;
        }

        if(type is MemberType memberType)
        {
            foreach(var abstractBase in memberType.BaseAbstractTypes.Where(abstractBase => TypeMemberLookup.Searches(memberType, abstractBase)))
            {
                yield return abstractBase;
            }
        }

        if(type is TypeParameter typeParameter)
        {
            foreach(var constraint in typeParameter.ConstraintsList.OfType<TypeConstraint>())
            {
                yield return constraint.ConstraintType;
            }
        }

        if(type is Newtype { SourceType: { } newtypeSource })
        {
            yield return newtypeSource;
        }
    }

    public static IMistType Unwrap(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Unwrap(aliased),
            RefinedType refined => Unwrap(refined.BaseType),
            LiftedType lifted => Unwrap(lifted.Type),
            UnknownValueType value => Unwrap(value.Domain),
            _ => type,
        };
    }
}