using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

// Generated carriers cannot expose a payload more widely than its declaration permits.
internal static class CarrierVisibility
{
    public static Accessibility Restrict(Accessibility declared, IMistType? instance)
    {
        return declared == Accessibility.Public && instance is not null && !IsPublic(instance) ? Accessibility.Internal : declared;
    }

    public static bool IsPublic(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => IsPublic(aliased),
            RefinedType refined => IsPublic(refined.BaseType),
            LiftedType lifted => IsPublic(lifted.Type),
            ConstructedType constructed => IsPublic(constructed.Definition) && constructed.TypeArguments.All(IsPublic)
                && (constructed.Enclosing is null || IsPublic(constructed.Enclosing)),
            ITupleType tuple => tuple.ElementTypes.All(IsPublic),
            UnionType union => union.Constituents.All(IsPublic),
            MemberType member => member.Modifiers.Accessibility == Accessibility.Public
                && (member.DeclaringType is null || IsPublic(member.DeclaringType)),
            EnumType enumeration => enumeration.Modifiers.Accessibility == Accessibility.Public
                && (enumeration.DeclaringType is null || IsPublic(enumeration.DeclaringType)),
            _ => true,
        };
    }
}