using Mist.ProgramModel.Contracts.Types;
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
        var queue = new Queue<(MistType Type, int Depth, bool Open)>();
        var seen = new HashSet<MistType>();

        queue.Enqueue((type, 0, false));
        _ = seen.Add(type);

        while(queue.Count > 0)
        {
            var (current, depth, open) = queue.Dequeue();

            AddTypeNames(current, request, depth, candidates);
            AddState(current, request, depth, open, candidates);
            AddMethods(current, request, depth, open, candidates);

            EnqueueBases(current, depth, open, queue, seen);
        }
    }

    /// <summary>
    /// Bases breadth-first, the concrete base before the declared abstracts. <paramref name="seen"/>
    /// makes a diamond one visit rather than two, and stops a cycle from hanging the walk.
    /// <para>
    /// A CONSTRUCTED base is walked through its definition, since that is where members are declared,
    /// and everything reached from there is flagged OPEN: the construction's arguments are known here
    /// but nothing in this layer can apply them, substitution belonging to
    /// <see cref="Construction.Binding.TypeSubstitution"/> in the layer above. Open once means open
    /// onwards, a base of an unsubstituted base being no more substituted than the base itself.
    /// </para>
    /// <para>
    /// A TYPE PARAMETER's nominal constraints are bases of it in the only sense that matters here.
    /// <c>where T : IOrderable</c> is the whole of what is known about a T, so it is exactly what a
    /// member access on one may see -- and being all that is known, it is also the only thing that
    /// could answer. A keyword or constructor constraint carries no members and contributes nothing.
    /// </para>
    /// </summary>
    private static void EnqueueBases(
        MistType type, int depth, bool open, Queue<(MistType, int, bool)> queue, HashSet<MistType> seen)
    {
        if(type is ConcreteType { BaseType: { } concreteBase })
        {
            Enqueue(concreteBase, depth, open, queue, seen);
        }

        if(type is MemberType memberType)
        {
            foreach(var abstractBase in memberType.BaseAbstractTypes)
            {
                Enqueue(abstractBase, depth, open, queue, seen);
            }
        }

        if(type is TypeParameter typeParameter)
        {
            foreach(var constraint in typeParameter.ConstraintsList)
            {
                if(constraint is TypeConstraint { ConstraintType: MistType constraintType })
                {
                    Enqueue(constraintType, depth, open, queue, seen);
                }
            }
        }
    }

    private static void Enqueue(
        MistType baseType, int depth, bool open, Queue<(MistType, int, bool)> queue, HashSet<MistType> seen)
    {
        var constructed = baseType as ConstructedType;

        if((constructed?.Definition ?? baseType) is not MistType definition || !seen.Add(definition))
        {
            return;
        }

        queue.Enqueue((definition, depth + 1, open || constructed is not null));
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

        if(type is IHasTypeParameters generic)
        {
            foreach(var typeParameter in generic.TypeParametersList)
            {
                if(typeParameter.Name == request.Name)
                {
                    candidates.Add(new TypeResult(typeParameter), depth);
                }
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
    private static void AddState(MistType type, LookupRequest request, int depth, bool open, CandidateSet candidates)
    {
        foreach(var field in FieldsOf(type))
        {
            if(field.Name == request.Name && Admits(field.Modifiers, request, depth) && !Withheld(open, field.Type))
            {
                candidates.Add(new FieldResult(field, type), depth);
            }
        }

        if(type is MemberType memberType)
        {
            foreach(var property in memberType.PropertiesList)
            {
                if(property.Name == request.Name && Admits(property.Modifiers, request, depth)
                    && !Withheld(open, property.Type))
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
    private static void AddMethods(MistType type, LookupRequest request, int depth, bool open, CandidateSet candidates)
    {
        if(type is not MemberType memberType)
        {
            return;
        }

        var group = new List<Method>();
        foreach(var method in memberType.MethodsList)
        {
            if(method.Name == request.Name && Admits(method.Modifiers, request, depth) && !Withheld(open, method))
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
    /// A member of an OPEN base is withheld when its type is stated in terms of a type parameter,
    /// because the answer would be that parameter rather than the argument standing for it. Answering
    /// it unsubstituted is the one outcome worse than not answering: the caller cannot tell an open
    /// <c>T</c> from a real one, so a wrong type would flow on silently where today a name simply does
    /// not resolve. Members whose types name no parameter are unaffected by the construction and are
    /// returned as they stand.
    /// </summary>
    private static bool Withheld(bool open, Method method)
    {
        if(!open)
        {
            return false;
        }

        if(Withheld(open, method.ReturnType))
        {
            return true;
        }

        foreach(var parameter in method.ParametersList)
        {
            if(Withheld(open, parameter.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Withheld(bool open, IMistType type)
    {
        if(!open)
        {
            return false;
        }

        if(type is TypeParameter)
        {
            return true;
        }

        if(type is ConstructedType constructed)
        {
            foreach(var argument in constructed.TypeArguments)
            {
                if(Withheld(open, argument))
                {
                    return true;
                }
            }
        }

        return false;
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
    /// A switch because <c>Modifiers</c> is declared independently on each kind that has one, with
    /// no base or interface in common. Kinds that cannot carry modifiers -- type parameters, slots,
    /// and the structural types -- are public, since nothing about them is hideable.
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