using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// What a name means on a type, read from the type itself.
/// <para>
/// Two callers ask, differing only in what they already know. An unqualified name reaches a
/// <see cref="TypeScope"/> mid-walk without knowing whether it is a local, a member, a type or a
/// namespace, and continues outward if this answers nothing. A qualified <c>receiver.X</c> knows
/// which type to ask and must not continue anywhere. The question itself is identical, so it lives
/// here once.
/// </para>
/// </summary>
internal static class TypeMemberLookup
{
    public static LookupResult Find(MistType type, LookupRequest request)
    {
        var candidates = new CandidateSet();
        Gather(type, request, candidates);
        return candidates.Resolve();
    }

    /// <summary>
    /// Adds every match on the type and its bases to an existing set, for a caller weighing
    /// candidates of its own alongside these.
    /// <para>
    /// The whole base graph is walked, never stopping early, because shadowing cannot be decided
    /// until every depth has been seen -- see <see cref="CandidateSet"/>.
    /// </para>
    /// </summary>
    public static void Gather(MistType type, LookupRequest request, CandidateSet candidates)
    {
        var queue = new Queue<(MistType Type, int Depth)>();
        var seen = new HashSet<MistType>();

        queue.Enqueue((type, 0));
        _ = seen.Add(type);

        while(queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            AddTypeNames(current, request, depth, candidates);
            AddState(current, request, depth, candidates);
            AddMethods(current, request, depth, candidates);

            EnqueueBases(current, depth, queue, seen);
        }
    }

    /// <summary>
    /// Bases breadth-first, the concrete base before the declared abstracts. <paramref name="seen"/>
    /// makes a diamond one visit rather than two, and stops a cycle from hanging the walk.
    /// </summary>
    private static void EnqueueBases(MistType type, int depth, Queue<(MistType, int)> queue, HashSet<MistType> seen)
    {
        if(type is ConcreteType { BaseType: { } concreteBase } && seen.Add(concreteBase))
        {
            queue.Enqueue((concreteBase, depth + 1));
        }

        if(type is MemberType memberType)
        {
            foreach(var abstractBase in memberType.BaseAbstractTypes)
            {
                if(seen.Add(abstractBase))
                {
                    queue.Enqueue((abstractBase, depth + 1));
                }
            }
        }
    }

    /// <summary>
    /// Nested types, and at depth 0 only, the type's own type parameters and abstract slots. A base's
    /// type parameters are bound by its own declaration: in <c>class Derived&lt;T&gt; :
    /// Base&lt;T&gt;</c> the <c>T</c> in scope is Derived's.
    /// </summary>
    private static void AddTypeNames(MistType type, LookupRequest request, int depth, CandidateSet candidates)
    {
        foreach(var nested in type.NestedTypes)
        {
            if(nested.Name == request.Name && IsVisible(AccessibilityOf(nested), depth))
            {
                candidates.Add(new TypeResult(nested), depth);
            }
        }

        if(depth != 0)
        {
            return;
        }

        foreach(var typeParameter in TypeParametersOf(type))
        {
            if(typeParameter.Name == request.Name)
            {
                candidates.Add(new TypeResult(typeParameter), depth);
            }
        }

        if(type is AbstractType abstractType)
        {
            foreach(var slot in abstractType.TypeSlotsList)
            {
                if(slot.Name == request.Name)
                {
                    candidates.Add(new TypeResult(slot), depth);
                }
            }
        }
    }

    /// <summary>
    /// Fields and properties, plus the two kinds reached by the same syntax without being stored as
    /// ordinary members: tuple elements and enum variants.
    /// </summary>
    private static void AddState(MistType type, LookupRequest request, int depth, CandidateSet candidates)
    {
        foreach(var field in FieldsOf(type))
        {
            if(field.Name == request.Name && Admits(field.Modifiers, request, depth))
            {
                candidates.Add(new FieldResult(field, type), depth);
            }
        }

        if(type is MemberType memberType)
        {
            foreach(var property in memberType.PropertiesList)
            {
                if(property.Name == request.Name && Admits(property.Modifiers, request, depth))
                {
                    candidates.Add(new PropertyResult(property, type), depth);
                }
            }
        }

        // An enum variant is named through the enum type, never through an instance, so it answers
        // to the same request a static member does.
        if(type is EnumType enumType && request.Permits(isStatic: true))
        {
            foreach(var member in enumType.MembersList)
            {
                if(member.Name == request.Name)
                {
                    candidates.Add(new EnumMemberResult(member, enumType), depth);
                }
            }
        }
    }

    /// <summary>
    /// Declared fields for a concrete type; for a tuple, the synthesized element fields, which are
    /// how <c>Item1</c> and a named element are reached.
    /// </summary>
    private static IReadOnlyList<Field> FieldsOf(MistType type)
    {
        return type switch
        {
            ConcreteType concrete => concrete.FieldsList,
            TupleType tuple => tuple.PositionalFields,
            NamedTupleType named => named.Fields,
            _ => [],
        };
    }

    /// <summary>
    /// The overloads declared at this depth, as a group. Constructors, operators and cast operators
    /// are excluded by construction: they live in their own lists because they are not reached by
    /// name.
    /// </summary>
    private static void AddMethods(MistType type, LookupRequest request, int depth, CandidateSet candidates)
    {
        if(type is not MemberType memberType)
        {
            return;
        }

        var group = new List<Method>();
        foreach(var method in memberType.MethodsList)
        {
            if(method.Name == request.Name && Admits(method.Modifiers, request, depth))
            {
                group.Add(method);
            }
        }

        if(group.Count > 0)
        {
            candidates.Add(new MethodGroupResult(group), depth);
        }
    }

    private static bool Admits(MemberModifiers modifiers, LookupRequest request, int depth)
    {
        return request.Permits(modifiers.IsStatic) && IsVisible(modifiers.Accessibility, depth);
    }

    /// <summary>
    /// Private members are visible only on the type that declared them. Coarser than C#: internal
    /// and protected are not distinguished, because the model has no assembly boundary to measure
    /// internal against.
    /// </summary>
    private static bool IsVisible(Accessibility accessibility, int depth)
    {
        return depth == 0 || accessibility != Accessibility.Private;
    }

    /// <summary>
    /// A switch because <c>TypeParametersList</c> is declared independently on each kind that has
    /// one, with no base or interface in common.
    /// </summary>
    private static List<TypeParameter> TypeParametersOf(MistType type)
    {
        return type switch
        {
            MemberType memberType => memberType.TypeParametersList,
            TypeAlias alias => alias.TypeParametersList,
            Newtype newtype => newtype.TypeParametersList,
            TypeSlot slot => slot.TypeParametersList,
            _ => [],
        };
    }

    /// <summary>
    /// A switch for the same reason as <see cref="TypeParametersOf"/>. Kinds that cannot carry
    /// modifiers -- type parameters, slots, and the structural types -- are public, since nothing
    /// about them is hideable.
    /// </summary>
    private static Accessibility AccessibilityOf(MistType type)
    {
        return type switch
        {
            MemberType memberType => memberType.Modifiers.Accessibility,
            TypeAlias alias => alias.Modifiers.Accessibility,
            Newtype newtype => newtype.Modifiers.Accessibility,
            EnumType enumType => enumType.Modifiers.Accessibility,
            _ => Accessibility.Public,
        };
    }
}