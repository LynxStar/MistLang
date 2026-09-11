using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Whether a type has a default an instance can be built from. An optional defaults to absence, an
/// intrinsic to what C# says, an enum to its marked variant; a class or struct defaults to an
/// instance the emitter synthesizes, which exists only when the type states a zero -- a default block
/// or a parameterless constructor -- its base leaves a parameterless constructor standing, and every
/// instance field has an answer. A type reached again through its own fields answers NOT defaultable:
/// a cycle means the default is infinitely deep, so there is no instance to build.
/// </summary>
internal static class Defaultability
{
    public static bool HasDefault(IMistType type)
    {
        return OptionalUnion.IsOptional(type)
            || type is ConcreteType { IsIntrinsic: true }
            || (type is EnumType { MembersList: { } variants } && variants.Exists(variant => variant.IsDefault))
            || (type is MemberType member && IsDefaultable(member));
    }

    public static bool IsDefaultable(MemberType type)
    {
        return IsDefaultable(type, []);
    }

    public static bool NeedsSentinel(ConcreteType type)
    {
        return type.ConstructorsList.Exists(c => !c.Modifiers.IsStatic);
    }

    public static IEnumerable<Field> InstanceFields(ConcreteType type)
    {
        return type.FieldsList.Where(field => !field.Modifiers.IsStatic);
    }

    public static DefaultEntry? BlockEntry(ConcreteType type, Field field)
    {
        if(type.DefaultBlock is null)
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
                && InstanceFields(concrete).All(field => CanDefault(concrete, field, visiting))
                ;
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }

    private static bool CanDefault(ConcreteType type, Field field, HashSet<MemberType> visiting)
    {
        var entry = BlockEntry(type, field);

        if(entry is DefaultBindingEntry binding)
        {
            return binding.BoundType is MemberType bound && IsDefaultable(bound, visiting);
        }

        return entry is not null
            || field.Initializer is not null
            || OptionalUnion.IsOptional(field.Type)
            || field.Type is ConcreteType { IsIntrinsic: true }
            || (field.Type is MemberType nested && IsDefaultable(nested, visiting))
            ;
    }

    private static bool StatesAZero(ConcreteType type)
    {
        return type.DefaultBlock is not null || HasParameterlessConstructor(type);
    }

    private static bool BaseIsDefaultable(ConcreteType type, HashSet<MemberType> visiting)
    {
        return type.BaseType is not MemberType baseType
            || (IsDefaultable(baseType, visiting)
                && baseType is ConcreteType concrete
                && HasParameterlessConstructor(concrete))
            ;
    }

    private static bool HasParameterlessConstructor(ConcreteType type)
    {
        return !NeedsSentinel(type)
            || type.ConstructorsList.Exists(c => c.ParametersList.Count == 0 && !c.Modifiers.IsStatic)
            ;
    }
}