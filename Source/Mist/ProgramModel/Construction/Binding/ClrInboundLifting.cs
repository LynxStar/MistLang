using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Closes a CLR member's result over <c>| void</c> once its type parameters are known.
/// <para>
/// THE DECISION IS SPLIT ACROSS TWO PHASES BECAUSE NEITHER HALF KNOWS BOTH FACTS. Projection knows
/// the member came from the CLR but not what <c>T</c> will be, and cannot wrap a parameter there
/// without making <c>List&lt;int&gt;</c>'s element optional too. Substitution knows what <c>T</c>
/// became but not where the member came from. <see cref="MemberModifiers.IsClrProjected"/> carries
/// the first fact to the second place.
/// </para>
/// <para>
/// Applied only where a member's type is a RESULT. Parameters, inference and alias expansion
/// substitute through the same machinery and must not lift: Mist has no null to send, and a union
/// on a parameter gives overload resolution more candidates to be ambiguous between.
/// </para>
/// </summary>
internal static class ClrInboundLifting
{
    public static IMistType Close(
        MemberModifiers modifiers, IMistType substituted, IMistType voidType, TypeInterner? interner)
    {
        if(!modifiers.IsClrProjected
            || modifiers.ClrResultIsNonNull
            || interner is null
            || !IsReferenceType(substituted))
        {
            return substituted;
        }

        return OptionalLifting.Lift(substituted, voidType, interner);
    }

    public static IMistType Close(
        LookupResult found, IMistType substituted, IMistType voidType, TypeInterner? interner)
    {
        var modifiers = found switch
        {
            FieldResult field => field.Field.Modifiers,
            PropertyResult property => property.Property.Modifiers,
            ExtensionPropertyResult extension => extension.Property.Modifiers,
            _ => default,
        };

        return Close(modifiers, substituted, voidType, interner);
    }

    private static bool IsReferenceType(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => IsReferenceType(constructed.Definition),
            TypeAlias { AliasedType: { } aliased } => IsReferenceType(aliased),
            ClassType or AbstractType => true,
            _ => false,
        };
    }
}