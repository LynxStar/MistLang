using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Whether a type has a default an instance can be built from. An optional defaults to absence, an
/// intrinsic to what C# says, an enum to its marked variant; a class or struct defaults to an
/// instance the emitter synthesizes, which exists only when the type states a zero -- a default block,
/// or no instance constructor of its own, a declared one being construction however few parameters it
/// takes -- its base has a default, which the derived default starts from without running any base
/// constructor, and every slot of <see cref="InstanceStorage"/> has an answer. A type reached again
/// through its own storage answers NOT defaultable: a cycle means the default is infinitely deep, so
/// there is no instance to build.
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
        return type is not RefinedType && HasZero(type, []);
    }

    public static bool IsDefaultable(MemberType type)
    {
        return IsDefaultable(type, []);
    }

    public static bool StorageHasDefault(ConcreteType type, InstanceStorage slot)
    {
        return CanDefault(type, slot, []);
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
            DefaultBindingEntry binding => binding.BoundType as MemberType,
            null when slot.Initializer is null => Unrefined(slot.Type) as MemberType,
            _ => null,
        };
    }

    public static ConcreteType? BaseWithoutDefault(ConcreteType type)
    {
        return type.BaseType is ConcreteType { IsIntrinsic: false, Assembly.Kind: not MistAssemblyKind.Ambient } baseType
            && !IsDefaultable(baseType)
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

    private static bool IsDefaultable(MemberType type, HashSet<MemberType> visiting)
    {
        if(!visiting.Add(type))
        {
            return false;
        }

        try
        {
            return type is ConcreteType { IsIntrinsic: false } concrete
                && StatesAZero(concrete)
                && BaseIsDefaultable(concrete, visiting)
                && InstanceStorage.Of(concrete).All(slot => CanDefault(concrete, slot, visiting))
                ;
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }

    private static bool CanDefault(ConcreteType type, InstanceStorage slot, HashSet<MemberType> visiting)
    {
        var entry = BlockEntry(type, slot);

        if(entry is DefaultBindingEntry binding)
        {
            return binding.BoundType is MemberType bound && IsDefaultable(bound, visiting);
        }

        return entry is not null || slot.Initializer is not null || HasZero(slot.Type, visiting);
    }

    private static bool HasZero(IMistType type, HashSet<MemberType> visiting)
    {
        return type is RefinedType refined
            ? HasZero(refined.BaseType, visiting)
            : OptionalUnion.IsOptional(type)
                || type is ConcreteType { IsIntrinsic: true }
                || (type is EnumType { MembersList: { } variants } && variants.Exists(variant => variant.IsDefault))
                || (type is MemberType member && IsDefaultable(member, visiting))
                ;
    }

    private static IMistType Unrefined(IMistType type)
    {
        return type is RefinedType refined ? Unrefined(refined.BaseType) : type;
    }

    private static bool BaseIsDefaultable(ConcreteType type, HashSet<MemberType> visiting)
    {
        return type.BaseType is not MemberType baseType || IsDefaultable(baseType, visiting);
    }
}