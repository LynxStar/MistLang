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
        var queue = new Queue<(MistType Type, int Depth, IReadOnlyList<ConstructedType> Path)>();
        var seen = new HashSet<MistType>();

        queue.Enqueue((type, 0, []));
        _ = seen.Add(type);

        while(queue.Count > 0)
        {
            var (current, depth, path) = queue.Dequeue();

            AddTypeNames(current, request, depth, path, candidates);
            AddState(current, request, depth, path, candidates);
            AddMethods(current, request, depth, path, candidates);

            EnqueueBases(current, depth, path, queue, seen);
        }
    }

    /// <summary>
    /// Bases breadth-first, the concrete base before the declared abstracts. <paramref name="seen"/>
    /// makes a diamond one visit rather than two, and stops a cycle from hanging the walk.
    /// <para>
    /// A CONSTRUCTED base is walked through its definition, since that is where members are declared,
    /// and the construction is RECORDED on the path so a caller can substitute through it -- nothing
    /// in this layer can apply it, substitution belonging to
    /// <see cref="Construction.Binding.TypeSubstitution"/> in the layer above. The path accumulates
    /// outward-to-inward, a base of a constructed base needing its ancestor's mapping as well as its
    /// own before either means anything.
    /// </para>
    /// <para>
    /// A TYPE PARAMETER's nominal constraints are bases of it in the only sense that matters here.
    /// <c>where T : IOrderable</c> is the whole of what is known about a T, so it is exactly what a
    /// member access on one may see -- and being all that is known, it is also the only thing that
    /// could answer. A keyword or constructor constraint carries no members and contributes nothing.
    /// </para>
    /// </summary>
    private static void EnqueueBases(
        MistType type,
        int depth,
        IReadOnlyList<ConstructedType> path,
        Queue<(MistType, int, IReadOnlyList<ConstructedType>)> queue,
        HashSet<MistType> seen)
    {
        if(type is ConcreteType { BaseType: { } concreteBase })
        {
            Enqueue(concreteBase, depth, path, queue, seen);
        }

        if(type is MemberType memberType)
        {
            foreach(var abstractBase in memberType.BaseAbstractTypes)
            {
                Enqueue(abstractBase, depth, path, queue, seen);
            }
        }

        if(type is TypeParameter typeParameter)
        {
            foreach(var constraint in typeParameter.ConstraintsList)
            {
                if(constraint is TypeConstraint { ConstraintType: MistType constraintType })
                {
                    Enqueue(constraintType, depth, path, queue, seen);
                }
            }
        }

        // A newtype forwards its source's members, so the source walks as a base does --
        // which is also what hides the source's private members, exactly as a base's are.
        // The newtype stays nominally distinct; only member LOOKUP reaches through.
        if(type is Newtype { SourceType: MistType newtypeSource })
        {
            Enqueue(newtypeSource, depth, path, queue, seen);
        }
    }

    private static void Enqueue(
        MistType baseType,
        int depth,
        IReadOnlyList<ConstructedType> path,
        Queue<(MistType, int, IReadOnlyList<ConstructedType>)> queue,
        HashSet<MistType> seen)
    {
        var constructed = baseType as ConstructedType;

        if((constructed?.Definition ?? baseType) is not MistType definition || !seen.Add(definition))
        {
            return;
        }

        queue.Enqueue((definition, depth + 1, constructed is null ? path : [.. path, constructed]));
    }

    /// <summary>
    /// Nested types, and at depth 0 only, the type's own type parameters and abstract slots. A base's
    /// type parameters are bound by its own declaration: in <c>class Derived&lt;T&gt; :
    /// Base&lt;T&gt;</c> the <c>T</c> in scope is Derived's.
    /// </summary>
    private static void AddTypeNames(
        MistType type, LookupRequest request, int depth, IReadOnlyList<ConstructedType> path, CandidateSet candidates)
    {
        foreach(var nested in type.NestedTypes)
        {
            if(nested.Name == request.Name && IsVisible(AccessibilityOf(nested), depth))
            {
                candidates.Add(new TypeResult(nested, path), depth);
            }
        }

        if(depth != 0)
        {
            return;
        }

        // A clone's body is its source's syntax, naming the source's parameters, which CloneScope answers;
        // the clone's own are named only by its newtype declaration.
        if(type is IHasTypeParameters generic and not ConcreteType { CloneSource: not null })
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
    private static void AddState(
        MistType type,
        LookupRequest request,
        int depth,
        IReadOnlyList<ConstructedType> path,
        CandidateSet candidates)
    {
        foreach(var field in FieldsOf(type))
        {
            if(field.Name == request.Name && Admits(field.Modifiers, request, depth))
            {
                candidates.Add(new FieldResult(field, type, path), depth);
            }
        }

        if(type is MemberType memberType)
        {
            foreach(var property in memberType.PropertiesList)
            {
                if(property.Name == request.Name && Admits(property.Modifiers, request, depth))
                {
                    candidates.Add(new PropertyResult(property, type, path), depth);
                }
            }
        }

        AddFixedArityProjections(type, request, candidates, depth, path);

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
    /// The projections a spread member has while its pack is still OPEN, which is possible exactly
    /// when the pack's domain fixes an arity: <c>Type[2]:TShapes</c> has two elements whatever they
    /// turn out to be, so <c>Shape1</c> is nameable inside the declaration though its type is not
    /// yet known. A pack of unknown length has no projections here -- there is no count to bound
    /// them by -- and gets them per construction instead.
    /// </summary>
    private static void AddFixedArityProjections(
        MistType type, LookupRequest request, CandidateSet candidates, int depth, IReadOnlyList<ConstructedType> path)
    {
        foreach(var field in FieldsOf(type))
        {
            if(ProjectedElement(field.Type, field.Name, request.Name) is { } element)
            {
                candidates.Add(
                    new FieldResult(
                        new Field(request.Name, element, field.Ownership, field.Modifiers), type, path),
                    depth);
            }
        }

        if(type is not MemberType memberType)
        {
            return;
        }

        foreach(var property in memberType.PropertiesList)
        {
            if(ProjectedElement(property.Type, property.Name, request.Name) is { } element)
            {
                candidates.Add(
                    new PropertyResult(
                        new Property(request.Name, element, property.Modifiers)
                        {
                            DeclaringType = property.DeclaringType,
                            Getter = property.Getter,
                            Setter = property.Setter,
                        },
                        type, path),
                    depth);
            }
        }
    }

    private static PackElementType? ProjectedElement(IMistType declared, string stem, string requested)
    {
        if(declared is not TupleType { ElementTypes: [SpreadType spread] }
            || spread.Operand is not TypeParameter { PackLength: { } length } pack
            || requested.Length <= stem.Length
            || !requested.StartsWith(stem, StringComparison.Ordinal)
            || !int.TryParse(requested.AsSpan(stem.Length), out var index)
            || index < 1
            || index > length)
        {
            return null;
        }

        return pack.ElementAt(index - 1);
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
            AnonymousType anonymous => anonymous.Fields,
            Newtype { ValueField: { } valueField } => [valueField],
            _ => [],
        };
    }

    /// <summary>
    /// The overloads declared at this depth, as a group. Constructors, operators and cast operators
    /// are excluded by construction: they live in their own lists because they are not reached by
    /// name.
    /// </summary>
    private static void AddMethods(
        MistType type,
        LookupRequest request,
        int depth,
        IReadOnlyList<ConstructedType> path,
        CandidateSet candidates)
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

        if(group.Count == 0)
        {
            return;
        }

        Dictionary<Method, IReadOnlyList<ConstructedType>>? paths = null;

        if(path.Count > 0)
        {
            paths = new Dictionary<Method, IReadOnlyList<ConstructedType>>(ReferenceEqualityComparer.Instance);

            foreach(var method in group)
            {
                paths[method] = path;
            }
        }

        candidates.Add(new MethodGroupResult(group, paths), depth);
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