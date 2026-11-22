using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

internal enum CloneKind
{
    Unchangeable,
    Declared,
    Derived,
    Refused,
}

internal sealed record CloneRefusal(string Why, bool Remedied);

internal sealed record CloneVerdict(CloneKind Kind, CloneRefusal? Refusal = null)
{
    public bool IsClonable => Kind is CloneKind.Declared or CloneKind.Derived;
}

/// <summary>
/// Whether a value of a type can be copied into one nothing else holds, and whether the compiler supplies that copy
/// (ownership-v2 Requirements 8.6, 8.7, 8.10, 8.11 and 8.12; design, Automatic Clone). Subclasses are read from every
/// type the program declares, because a class derives only when each of them could be copied too. A type met again
/// while it is still being judged counts as derivable, so a graph holding itself through owned fields derives, and
/// what was admitted on that assumption is withdrawn if it fails. Unchangeable here is the glossary's, which counts a
/// value type only when nothing it holds can change; <c>Origins.IsUnchangeable</c> answers provenance and counts every
/// value type.
/// </summary>
internal sealed class CloneDerivation
{
    private readonly Dictionary<ConcreteType, List<ConcreteType>> subclasses = new(ReferenceEqualityComparer.Instance);

    public CloneDerivation(IEnumerable<MistType> types)
    {
        foreach(var type in types.OfType<ConcreteType>())
        {
            if(DefinitionOf(type.BaseType) is ConcreteType baseType && type is ClassType)
            {
                if(!subclasses.TryGetValue(baseType, out var known))
                {
                    subclasses[baseType] = known = [];
                }

                known.Add(type);
            }
        }
    }

    public bool IsEmpty => subclasses.Count == 0;

    public CloneVerdict Judge(IMistType type)
    {
        return new Session(this, assumed: null).Judge(type);
    }

    public bool Derives(ConcreteType definition)
    {
        return Candidate(definition) && new Session(this, definition).Derivation(Itself(definition)) is null;
    }

    public static string Describe(IMistType type, CloneRefusal refusal)
    {
        var name = TypeDisplay.Mist(type);

        return refusal.Remedied
            ? $"'{name}' cannot be cloned: {refusal.Why}; make that field 'owned' or give the type a 'Clone()' of its own"
            : $"'{name}' cannot be cloned: {refusal.Why}";
    }

    public static bool DeclaresClone(MemberType type)
    {
        return type.MethodsList.Exists(method => method is { Name: "Clone", ParametersList.Count: 0, IsDerivedClone: false, Modifiers.IsStatic: false });
    }

    public static bool IsInMist(MistType? type)
    {
        return type is ConcreteType { IsIntrinsic: false } concrete && (concrete.DeclarationNode is not null || concrete.CloneSource is not null);
    }

    private static bool Candidate(ConcreteType type)
    {
        return IsInMist(type) && !type.Modifiers.IsStatic && !DeclaresClone(type);
    }

    private static IMistType Itself(ConcreteType definition)
    {
        return definition.TypeParametersList.Count == 0 ? definition : new ConstructedType(definition, definition.TypeParametersList);
    }

    private static MistType? DefinitionOf(IMistType? type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) as MistType;
    }

    private static string Slot(InstanceStorage slot, ConcreteType declaring)
    {
        return $"{(slot.Property is null ? "field" : "property")} '{slot.Name}' of '{TypeDisplay.Mist(declaring)}'";
    }

    private static bool IsOwned(InstanceStorage slot, ConcreteType declaring)
    {
        return slot.Field is { } field
            ? OwnedProvenance.IsOwned(field)
            : OwnedProvenance.IsOwned(slot.Property!) || declaring.Modifiers.IsOwned;
    }

    private sealed class Session(CloneDerivation world, ConcreteType? assumed)
    {
        private readonly HashSet<string> pending = new(StringComparer.Ordinal);

        private readonly Dictionary<string, CloneRefusal?> settled = new(StringComparer.Ordinal);

        private readonly List<string> provisional = [];

        private int depth;

        public CloneVerdict Judge(IMistType type)
        {
            type = Unwrapped(type);

            if(Unchangeable(type, []))
            {
                return new CloneVerdict(CloneKind.Unchangeable);
            }

            switch(type)
            {
                case UnionType union:
                    var refused = union.Constituents.Where(constituent => !OptionalUnion.IsVoid(constituent))
                        .Select(Judge).FirstOrDefault(verdict => verdict.Kind == CloneKind.Refused);
                    return refused ?? new CloneVerdict(CloneKind.Declared);

                case TypeParameter parameter:
                    return Assumed(parameter) || GuaranteesClonable(parameter)
                        ? new CloneVerdict(CloneKind.Declared)
                        : Refuse($"type parameter '{parameter.Name}' is not constrained to 'IClonable<{parameter.Name}>'");
            }

            if(DefinitionOf(type) is ConcreteType concrete && IsInMist(concrete))
            {
                if(DeclaresClone(concrete))
                {
                    return new CloneVerdict(CloneKind.Declared);
                }

                return Derivation(type) is { } refusal ? new CloneVerdict(CloneKind.Refused, refusal) : new CloneVerdict(CloneKind.Derived);
            }

            return DefinitionOf(type) is { } definition && (ConformsToClonable(definition) || (definition is MemberType member && DeclaresClone(member)))
                ? new CloneVerdict(CloneKind.Declared)
                : Refuse("it implements no 'IClonable<T>'");
        }

        private static CloneVerdict Refuse(string why)
        {
            return new CloneVerdict(CloneKind.Refused, new CloneRefusal(why, Remedied: false));
        }

        public CloneRefusal? Derivation(IMistType type)
        {
            var key = type.FullyQualifiedName;

            if(settled.TryGetValue(key, out var known))
            {
                return known;
            }

            if(!pending.Add(key))
            {
                return null;
            }

            var mark = provisional.Count;

            try
            {
                var refusal = Fields(type) ?? Subclasses(type);

                if(refusal is null)
                {
                    provisional.Add(key);
                }
                else
                {
                    for(var i = mark; i < provisional.Count; i++)
                    {
                        _ = settled.Remove(provisional[i]);
                    }

                    provisional.RemoveRange(mark, provisional.Count - mark);
                }

                settled[key] = refusal;
                return refusal;
            }
            finally
            {
                _ = pending.Remove(key);
            }
        }

        private CloneRefusal? Fields(IMistType type)
        {
            var current = (ConcreteType)DefinitionOf(type)!;
            var substitution = TypeSubstitution.ForReceiver(type as ConstructedType);

            while(true)
            {
                if(current.FieldsList.Exists(field => field.Name == "Clone") || current.PropertiesList.Exists(property => property.Name == "Clone"))
                {
                    return new CloneRefusal($"'{TypeDisplay.Mist(current)}' declares a member named 'Clone' that is not a method", Remedied: false);
                }

                foreach(var slot in InstanceStorage.Of(current))
                {
                    if(Meets(substitution.Apply(slot.Type, interner: null), IsOwned(slot, current)) is { } why)
                    {
                        return new CloneRefusal($"{Slot(slot, current)} {why}", Remedied: true);
                    }
                }

                if(current.BaseType is not { } written)
                {
                    return null;
                }

                var baseType = substitution.Apply(written, interner: null);

                if(DefinitionOf(baseType) is not ConcreteType next || !IsInMist(next))
                {
                    return new CloneRefusal($"its base class '{TypeDisplay.Mist(baseType)}' is not declared in Mist, so what it holds cannot be seen", Remedied: false);
                }

                if(DeclaresClone(next))
                {
                    return new CloneRefusal(
                        $"its base class '{TypeDisplay.Mist(next)}' declares 'Clone()', which copies every value as class '{TypeDisplay.Mist(next)}', and which '{TypeDisplay.Mist(DefinitionOf(type)!)}' cannot declare again",
                        Remedied: false);
                }

                substitution = TypeSubstitution.ForReceiver(baseType as ConstructedType);
                current = next;
            }
        }

        private CloneRefusal? Subclasses(IMistType type)
        {
            var definition = (ConcreteType)DefinitionOf(type)!;
            var visited = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<ConcreteType>([definition]);

            while(queue.Count > 0)
            {
                foreach(var subclass in world.subclasses.GetValueOrDefault(queue.Dequeue()) ?? [])
                {
                    if(!visited.Add(subclass))
                    {
                        continue;
                    }

                    queue.Enqueue(subclass);
                    var name = TypeDisplay.Mist(subclass);

                    if(DeclaresClone(subclass))
                    {
                        return new CloneRefusal($"its subclass '{name}' declares a 'Clone()' of its own", Remedied: false);
                    }

                    var substitution = SubclassSubstitution(subclass, definition, type);

                    foreach(var slot in InstanceStorage.Of(subclass))
                    {
                        if(Meets(substitution.Apply(slot.Type, interner: null), IsOwned(slot, subclass)) is { } why)
                        {
                            return new CloneRefusal($"its subclass '{name}' cannot be copied, because {Slot(slot, subclass)} {why}", Remedied: true);
                        }
                    }
                }
            }

            return null;
        }

        private static TypeSubstitution SubclassSubstitution(ConcreteType subclass, ConcreteType definition, IMistType judged)
        {
            var substitution = TypeSubstitution.Empty;

            if(judged is not ConstructedType constructed || subclass.TypeParametersList.Count == 0)
            {
                return substitution;
            }

            IMistType? step = subclass.BaseType;

            while(step is not null && !ReferenceEquals(DefinitionOf(step), definition))
            {
                step = DefinitionOf(step) is ConcreteType { BaseType: { } further }
                    ? TypeSubstitution.ForReceiver(step as ConstructedType).Apply(further, interner: null)
                    : null;
            }

            if(step is not ConstructedType reached)
            {
                return substitution;
            }

            for(var i = 0; i < reached.TypeArguments.Count && i < constructed.TypeArguments.Count; i++)
            {
                if(reached.TypeArguments[i] is TypeParameter parameter && subclass.TypeParametersList.Contains(parameter))
                {
                    substitution = substitution.With(parameter, constructed.TypeArguments[i]);
                }
            }

            return substitution;
        }

        private string? Meets(IMistType type, bool owned)
        {
            type = Unwrapped(type);

            if(ResultTransfers.Payloads(type) is { Count: > 0 } payloads)
            {
                return payloads.Select(payload => Meets(payload, owned)).FirstOrDefault(why => why is not null);
            }

            if(Unchangeable(type, []))
            {
                return null;
            }

            switch(type)
            {
                case UnionType union:
                    return union.Constituents.Where(constituent => !OptionalUnion.IsVoid(constituent))
                        .Select(constituent => Meets(constituent, owned)).FirstOrDefault(why => why is not null);

                case ITupleType tuple:
                    return tuple.ElementTypes.Select(element => Meets(element, owned)).FirstOrDefault(why => why is not null);

                case TypeParameter parameter:
                    return Assumed(parameter) || (owned && GuaranteesClonable(parameter))
                        ? null
                        : $"holds type parameter '{parameter.Name}', which is not constrained so that every argument can be copied";
            }

            depth++;
            var verdict = owned ? Judge(type) : null;
            depth--;

            if(verdict is { IsClonable: true })
            {
                return null;
            }

            if(DefinitionOf(type) is StructType structType && IsInMist(structType) && Derivation(type) is null)
            {
                return null;
            }

            var held = TypeDisplay.Mist(type);

            return owned
                ? $"is owned but holds '{held}', which cannot be cloned{(depth == 0 && verdict?.Refusal is { } nested ? $" ({nested.Why})" : "")}"
                : $"is not owned and holds '{held}', which can change";
        }

        private bool Assumed(TypeParameter parameter)
        {
            for(MistType? declaring = assumed; declaring is not null; declaring = declaring.DeclaringType)
            {
                if(declaring is IHasTypeParameters generic && generic.TypeParametersList.Contains(parameter))
                {
                    return true;
                }
            }

            return false;
        }

        private bool GuaranteesClonable(TypeParameter parameter)
        {
            return parameter.ConstraintsList.Exists(constraint => constraint is TypeConstraint { ConstraintType: var bound }
                && (bound is TypeParameter nested ? Assumed(nested) || GuaranteesClonable(nested) : ConformsToClonable(DefinitionOf(bound))));
        }

        private bool Unchangeable(IMistType type, HashSet<string> visiting)
        {
            type = Unwrapped(type);

            if(ResultTransfers.Payloads(type) is { Count: > 0 } payloads)
            {
                return payloads.All(payload => Unchangeable(payload, visiting));
            }

            switch(type)
            {
                case UnionType union:
                    return union.Constituents.All(constituent => Unchangeable(constituent, visiting));

                case INamedTupleType named:
                    return Unchangeable(named.Underlying, visiting);

                case ITupleType tuple:
                    return tuple.ElementTypes.All(element => Unchangeable(element, visiting));

                case EnumType:
                    return true;

                case TypeParameter parameter:
                    return Assumed(parameter);
            }

            if(OptionalUnion.IsVoid(type) || string.Equals(type.FullyQualifiedName, WellKnownTypes.String.FullyQualifiedName, StringComparison.Ordinal))
            {
                return true;
            }

            var definition = DefinitionOf(type) as ConcreteType;

            if(definition is null || !visiting.Add(type.FullyQualifiedName))
            {
                return definition is not null;
            }

            if(!IsInMist(definition))
            {
                return definition is StructType { IsRef: false } structType && !(structType.IsIntrinsic && !structType.Modifiers.IsReadonly)
                    ? (type as ConstructedType)?.TypeArguments.All(argument => Unchangeable(argument, visiting)) ?? true
                    : definition is { IsIntrinsic: true, Modifiers.IsReadonly: true } and not StructType;
            }

            if(definition is StructType { IsRef: true } or ClassType { Modifiers.IsReadonly: false })
            {
                return false;
            }

            var substitution = TypeSubstitution.ForReceiver(type as ConstructedType);

            for(var current = definition; current is not null;)
            {
                if(InstanceStorage.Of(current).Any(slot => !Unchangeable(substitution.Apply(slot.Type, interner: null), visiting)))
                {
                    return false;
                }

                var baseType = current.BaseType is { } written ? substitution.Apply(written, interner: null) : null;
                substitution = TypeSubstitution.ForReceiver(baseType as ConstructedType);
                current = DefinitionOf(baseType) as ConcreteType;
            }

            return definition is StructType || world.subclasses.GetValueOrDefault(definition) is not { Count: > 0 } descendants
                || descendants.TrueForAll(subclass => Unchangeable(Itself(subclass), visiting));
        }

        private static IMistType Unwrapped(IMistType type)
        {
            return type switch
            {
                TypeAlias { AliasedType: { } aliased } => Unwrapped(aliased),
                ConstructedType { Definition: TypeAlias { AliasedType: { } aliased } } alias
                    => Unwrapped(TypeSubstitution.ForReceiver(alias).Apply(aliased, interner: null)),
                RefinedType refined => Unwrapped(refined.BaseType),
                LiftedType lifted => Unwrapped(lifted.Type),
                Newtype { SourceType: { } source } => Unwrapped(source),
                _ => type,
            };
        }

        private static bool ConformsToClonable(MistType? definition)
        {
            return definition is not null
                && (definition.FullyQualifiedName == WellKnownTypes.IClonable.FullyQualifiedName
                    || (definition is MemberType member && member.BaseAbstractTypes.Exists(abstractType => ConformsToClonable(DefinitionOf(abstractType))))
                    || (definition is ConcreteType concrete
                        && (concrete.ConformancesList.Exists(conformance => ConformsToClonable(DefinitionOf(conformance.AbstractType)))
                            || ConformsToClonable(DefinitionOf(concrete.BaseType)))));
        }
    }
}