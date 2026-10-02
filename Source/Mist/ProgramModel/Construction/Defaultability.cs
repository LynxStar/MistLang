using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Whether a type has a default an instance can be built from. An optional defaults to absence, an
/// intrinsic to what C# says, an enum to its marked variant; a class or struct defaults to an
/// instance the emitter synthesizes, which exists only when the type states a zero -- a default block,
/// or no instance constructor of its own, a declared one being construction however few parameters it
/// takes -- its base has a default, which the derived default starts from without running any base
/// constructor, and every slot of <see cref="InstanceStorage"/> has an answer. A construction of a
/// generic class or struct answers as its definition does with its arguments substituted, and a
/// construction of an intrinsic definition has none. A DECLARATION is judged for some arguments, every
/// type parameter taken to have a zero, since its refusal, its factory and the purity of its
/// initializers serve every instantiation; a use is judged for its own arguments, where a type
/// parameter has a zero only under <c>where T : default</c>. A definition whose default needs its own
/// definition's default again is infinitely deep for every argument, so there is no instance to build,
/// and deciding that on definitions before expanding any instantiation is what makes expansion end.
/// </summary>
internal static class Defaultability
{
    /// <summary>
    /// Never true of a refinement, although storage of one starts from its base's zero: there
    /// DefaultabilityAnalyzer asks the predicate whether it admits that zero, and nothing asks it for a
    /// <c>default</c> of the refinement or a <c>default</c> constraint closed over one.
    /// </summary>
    public static bool HasDefault(IMistType type)
    {
        return type is not RefinedType && HasZero(type, assumed: false);
    }

    public static bool IsDefaultable(MemberType type)
    {
        return type is ConcreteType concrete && IsDefaultable(concrete, TypeSubstitution.Empty, assumed: true);
    }

    public static bool StorageHasDefault(ConcreteType type, InstanceStorage slot)
    {
        return CanDefault(type, slot, TypeSubstitution.Empty, assumed: true);
    }

    public static ConcreteType? DeclarationOf(IMistType type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) as ConcreteType is { IsIntrinsic: false } declaration
            ? declaration
            : null;
    }

    public static IReadOnlyList<TypeParameter> ZeroNeeded(ConcreteType declaration)
    {
        var needed = Needed(declaration, new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance));

        return InScope(declaration).SelectMany(parameter => ZeroesOf(parameter, needed)).ToList();
    }

    /// <summary>
    /// What a declared parameter contributes to <see cref="ZeroNeeded"/>: itself, or for a pack each
    /// element storage is typed by (<c>TArgs[0]</c>), in index order. A pack has no zero of its own;
    /// its elements are closed separately, so each owes one separately.
    /// </summary>
    private static IEnumerable<TypeParameter> ZeroesOf(TypeParameter parameter, HashSet<TypeParameter> needed)
    {
        return needed.Contains(parameter)
            ? [parameter]
            : needed.OfType<PackElementType>().Where(element => ReferenceEquals(element.Pack, parameter)).OrderBy(element => element.Index);
    }

    public static bool IsDefaultConstrained(TypeParameter parameter)
    {
        return parameter.ConstraintsList.Exists(constraint => constraint is KeywordConstraint { Kind: TypeParameterConstraintKind.Default });
    }

    public static bool NeedsSentinel(ConcreteType type)
    {
        return type.ConstructorsList.Exists(c => !c.Modifiers.IsStatic);
    }

    public static bool StatesAZero(ConcreteType type)
    {
        return type.DefaultBlock is not null || !NeedsSentinel(type);
    }

    public static IEnumerable<InstanceStorage> StorageWithoutDefault(ConcreteType type)
    {
        return InstanceStorage.Of(type).Where(slot => !IsUndecided(slot.Type) && !StorageHasDefault(type, slot));
    }

    public static bool StartsFromZero(ConcreteType type, InstanceStorage slot)
    {
        return BlockEntry(type, slot) is null && slot.Initializer is null;
    }

    public static MemberType? AnsweredThrough(ConcreteType type, InstanceStorage slot)
    {
        return BlockEntry(type, slot) switch
        {
            DefaultBindingEntry binding => Declared(binding.BoundType),
            null when slot.Initializer is null => Declared(Unrefined(slot.Type)),
            _ => null,
        };
    }

    public static IMistType? BaseWithoutDefault(ConcreteType type)
    {
        return type.BaseType is { } baseType
            && DeclarationOf(baseType) is { Assembly.Kind: not MistAssemblyKind.Ambient }
            && !BaseHasDefault(type, TypeSubstitution.Empty, assumed: true)
            ? baseType
            : null;
    }

    public static bool IsUndecided(IMistType type)
    {
        return type is TypeParameter or SpreadType or PackElementType
            || (type is TupleType tuple && tuple.ElementTypes.Any(IsUndecided));
    }

    public static IEnumerable<Field> InstanceFields(ConcreteType type)
    {
        return type.FieldsList.Where(field => !field.Modifiers.IsStatic);
    }

    public static DefaultEntry? BlockEntry(ConcreteType type, InstanceStorage slot)
    {
        if(type.DefaultBlock is null || slot.Field is not { } field)
        {
            return null;
        }

        foreach(var entry in type.DefaultBlock.Entries)
        {
            if(entry.FieldName == field.Name)
            {
                return entry;
            }
        }

        return null;
    }

    private static bool IsDefaultable(ConcreteType type, TypeSubstitution arguments, bool assumed)
    {
        return !type.IsIntrinsic
            && StatesAZero(type)
            && !Reaches(type, type, new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance))
            && BaseHasDefault(type, arguments, assumed)
            && InstanceStorage.Of(type).All(slot => CanDefault(type, slot, arguments, assumed))
            ;
    }

    private static bool CanDefault(ConcreteType type, InstanceStorage slot, TypeSubstitution arguments, bool assumed)
    {
        var entry = BlockEntry(type, slot);

        if(entry is DefaultBindingEntry binding)
        {
            return binding.BoundType is { } bound && Synthesizes(arguments.Apply(bound, interner: null), assumed);
        }

        return entry is not null || slot.Initializer is not null || HasZero(arguments.Apply(slot.Type, interner: null), assumed);
    }

    private static bool HasZero(IMistType type, bool assumed)
    {
        if(type is RefinedType refined)
        {
            return HasZero(refined.BaseType, assumed);
        }

        if(type is TypeParameter parameter)
        {
            return assumed || IsDefaultConstrained(parameter);
        }

        return OptionalUnion.IsOptional(type)
            || type is ConcreteType { IsIntrinsic: true }
            || (type is EnumType { MembersList: { } variants } && variants.Exists(variant => variant.IsDefault))
            || Synthesizes(type, assumed)
            ;
    }

    private static bool Synthesizes(IMistType type, bool assumed)
    {
        return type switch
        {
            ConstructedType constructed => DeclarationOf(constructed) is { } declaration
                && IsDefaultable(declaration, TypeSubstitution.ForReceiver(constructed), assumed),
            ConcreteType concrete => IsDefaultable(concrete, TypeSubstitution.Empty, assumed),
            _ => false,
        };
    }

    private static bool BaseHasDefault(ConcreteType type, TypeSubstitution arguments, bool assumed)
    {
        return type.BaseType is not (MemberType or ConstructedType) || Synthesizes(arguments.Apply(type.BaseType, interner: null), assumed);
    }

    private static IEnumerable<IMistType> ZeroSources(ConcreteType type)
    {
        foreach(var slot in InstanceStorage.Of(type))
        {
            var entry = BlockEntry(type, slot);

            if(entry is DefaultBindingEntry { BoundType: { } bound })
            {
                yield return bound;
            }
            else if(entry is null && slot.Initializer is null)
            {
                yield return Unrefined(slot.Type);
            }
        }

        if(type.BaseType is MemberType or ConstructedType)
        {
            yield return type.BaseType;
        }
    }

    private static HashSet<TypeParameter> Needed(ConcreteType declaration, HashSet<ConcreteType> visiting)
    {
        var needed = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);

        if(visiting.Add(declaration))
        {
            foreach(var written in ZeroSources(declaration))
            {
                needed.UnionWith(NeededBy(written, visiting));
            }

            _ = visiting.Remove(declaration);
        }

        return needed;
    }

    private static IEnumerable<TypeParameter> NeededBy(IMistType type, HashSet<ConcreteType> visiting)
    {
        return type switch
        {
            TypeParameter parameter => [parameter],
            RefinedType refined => NeededBy(refined.BaseType, visiting),
            ConstructedType constructed when DeclarationOf(constructed) is { } declaration => Needed(declaration, visiting)
                .SelectMany(parameter => NeededBy(TypeSubstitution.ForReceiver(constructed).Apply(parameter, interner: null), visiting))
                .ToList(),
            _ => [],
        };
    }

    private static bool Reaches(ConcreteType from, ConcreteType target, HashSet<ConcreteType> visited)
    {
        foreach(var next in ZeroSources(from).SelectMany(DeclarationsNeededBy))
        {
            if(ReferenceEquals(next, target) || (visited.Add(next) && Reaches(next, target, visited)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ConcreteType> DeclarationsNeededBy(IMistType type)
    {
        if(DeclarationOf(type) is not { } declaration)
        {
            return [];
        }

        if(type is not ConstructedType constructed)
        {
            return [declaration];
        }

        var arguments = TypeSubstitution.ForReceiver(constructed);

        return ZeroNeeded(declaration)
            .SelectMany(parameter => DeclarationsNeededBy(Unrefined(arguments.Apply(parameter, interner: null))))
            .Prepend(declaration);
    }

    private static IEnumerable<TypeParameter> InScope(MemberType type)
    {
        return (type.DeclaringType is MemberType enclosing ? InScope(enclosing) : []).Concat(type.TypeParametersList);
    }

    private static MemberType? Declared(IMistType? type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) as MemberType;
    }

    private static IMistType Unrefined(IMistType type)
    {
        return type is RefinedType refined ? Unrefined(refined.BaseType) : type;
    }
}