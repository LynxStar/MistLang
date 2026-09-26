using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What a declaration says about itself, read uniformly across the kinds that say it differently. The
/// EFFECTIVE accessibility of a type is the narrowest along its nesting, and of anything structural the
/// narrowest of what it is built from: a public method returning <c>Box&lt;Hidden&gt;</c> exposes Hidden as
/// surely as one returning Hidden. Internal and protected are incomparable, as C# has them, so each is
/// at least as accessible only as itself and private.
/// </summary>
internal static class Declarations
{
    public static SyntaxNode? NodeOf(DeclaredType type)
    {
        return type switch
        {
            MemberType member => member.DeclarationNode,
            TypeAlias alias => alias.DeclarationNode,
            Newtype newtype => newtype.DeclarationNode,
            EnumType enumType => enumType.DeclarationNode,
            _ => null,
        };
    }

    public static MemberModifiers? ModifiersOf(IMistType type)
    {
        return type switch
        {
            MemberType member => member.Modifiers,
            TypeAlias alias => alias.Modifiers,
            Newtype newtype => newtype.Modifiers,
            EnumType enumType => enumType.Modifiers,
            _ => null,
        };
    }

    public static Accessibility Effective(IMistType type)
    {
        return type is DeclaredType declared
            ? Narrowest(
                ModifiersOf(declared)?.Accessibility ?? Accessibility.Public,
                declared.DeclaringType is { } outer ? Effective(outer) : Accessibility.Public)
            : Narrowest(Accessibility.Public, Parts(type).Select(Effective));
    }

    public static IEnumerable<IMistType> Parts(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => [constructed.Definition, .. constructed.TypeArguments],
            UnionType union => union.Constituents,
            IFunctionType function => [function.ReturnType, .. function.ParameterTypes],
            INamedTupleType named => [named.Underlying],
            ITupleType tuple => tuple.ElementTypes,
            RefinedType refined => [refined.BaseType],
            LiftedType lifted => [lifted.Type],
            UnknownValueType value => [value.Domain],
            _ => [],
        };
    }

    public static bool IsAtLeast(Accessibility exposed, Accessibility required)
    {
        return exposed == Accessibility.Public
            || required == Accessibility.Private
            || exposed == required;
    }

    public static bool IsReadonly(IMistType type)
    {
        return type switch
        {
            EnumType => true,
            Newtype { SourceType: { } source } => IsReadonly(source),
            TypeAlias { AliasedType: { } aliased } => IsReadonly(aliased),
            RefinedType refined => IsReadonly(refined.BaseType),
            ConstructedType constructed => IsReadonly(constructed.Definition),
            MemberType member => member.Modifiers.IsReadonly,
            _ => false,
        };
    }

    public static bool IsSealed(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => IsSealed(constructed.Definition),
            MemberType member => member.Modifiers.IsSealed,
            _ => false,
        };
    }

    private static Accessibility Narrowest(Accessibility first, IEnumerable<Accessibility> rest)
    {
        return rest.Aggregate(first, Narrowest);
    }

    public static Accessibility Narrowest(Accessibility left, Accessibility right)
    {
        if(IsAtLeast(left, right))
        {
            return right;
        }

        return IsAtLeast(right, left) ? left : Accessibility.Private;
    }
}