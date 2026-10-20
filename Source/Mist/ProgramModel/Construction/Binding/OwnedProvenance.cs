using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Where an owned position gets its value, and what to do when the source does not already own one.
/// An owned binding promises an INDEPENDENT value, so a source that something else still holds has to
/// be copied -- otherwise the two names alias and mutating either is visible through the other, which
/// is the whole of what owning is meant to rule out. An owned field read through a shared holder is the
/// same promise seen from the other side (owned-fields Requirement 2.3): the holder cannot give the
/// field away, so what it hands out is a copy.
/// </summary>
internal static class OwnedProvenance
{
    /// <summary>
    /// The expression to bind at a position of the given tier: the source itself where it already owns
    /// its value or the position only lends, and a clone of it otherwise. A type with no <c>Clone</c>
    /// is left alone -- whether owning it is legal at all is a judgement on a finished model, and
    /// refusing here would turn a rule about provenance into one about which types may be owned.
    /// </summary>
    public static Expression At(OwnershipKind target, Expression source, bool byReference = false)
    {
        if(byReference || target is OwnershipKind.Frozen or OwnershipKind.Borrowed || !Clonable(source.Type))
        {
            return source;
        }

        return (target == OwnershipKind.Owned && !Owns(source)) || SharedExtraction(source) is not null
            ? new CloneExpression(source.Node, source)
            : source
            ;
    }

    /// <summary>
    /// The arguments of a call, each read against the tier its parameter declares. An owned parameter
    /// is the same promise an owned binding makes, made across a call instead of into a name.
    /// </summary>
    public static IReadOnlyList<Expression> AtParameters(
        IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        if(parameters is null)
        {
            return arguments;
        }

        var adjusted = new List<Expression>(arguments.Count);

        for(var i = 0; i < arguments.Count; i++)
        {
            adjusted.Add(i < parameters.Count
                ? At(parameters[i].Modifier.Ownership, arguments[i], parameters[i].Modifier.IsRef)
                : arguments[i]);
        }

        return adjusted;
    }

    public static OwnershipKind OwnershipOf(Expression target)
    {
        return target switch
        {
            LocalReferenceExpression { Local: { IsRef: false } local } => local.Ownership,
            FieldReferenceExpression { Field: var field } => IsOwned(field) ? OwnershipKind.Owned : OwnershipKind.Unrestricted,
            BackingFieldReferenceExpression { Property: var backed } => IsOwned(backed) ? OwnershipKind.Owned : OwnershipKind.Unrestricted,
            PropertyReferenceExpression { Property: var property } => IsOwned(property) ? OwnershipKind.Owned : OwnershipKind.Unrestricted,
            MemberAccessExpression { Member: var member } => OwnershipOf(member),
            _ => OwnershipKind.Unrestricted,
        };
    }

    public static OwnershipKind OwnershipOf(LookupResult? member)
    {
        return member switch
        {
            FieldResult { Field: var field } when IsOwned(field) => OwnershipKind.Owned,
            PropertyResult { Property: var property } when IsOwned(property) => OwnershipKind.Owned,
            _ => OwnershipKind.Unrestricted,
        };
    }

    public static bool IsOwned(Field field)
    {
        return !field.Modifiers.IsStatic
            && (field.Ownership == OwnershipKind.Owned || field.Modifiers.IsOwned || field.DeclaringType is { Modifiers.IsOwned: true });
    }

    public static bool IsOwned(Property property)
    {
        return property.Modifiers is { IsOwned: true, IsStatic: false };
    }

    public static (Field Field, LocalVariable Holder)? SharedExtraction(Expression source)
    {
        return source is MemberAccessExpression
        {
            Receiver: LocalReferenceExpression { Local: { Ownership: OwnershipKind.Unrestricted, IsRef: false } holder },
            Member: FieldResult { Field: var field },
        } && IsOwned(field)
            ? (field, holder)
            : null;
    }

    /// <summary>
    /// Whether the source hands over a value nothing else holds. A construction is fresh, a clone is
    /// fresh by definition, a member declaring an owned return says so, and a binding declared owned
    /// carries it. Everything else is a value someone else may still be reading.
    /// </summary>
    private static bool Owns(Expression source)
    {
        return source switch
        {
            NewExpression or CloneExpression or ArrayCreationExpression => true,

            // An EXPLICIT clone is owned provenance. Without this a written `x.Clone()` assigned to an
            // owned binding is cloned again, which classic does and which copies twice to reach a
            // value the first copy already produced.
            InvocationExpression { Method.Name: "Clone", Method.ParametersList.Count: 0 } => true,

            // BOTH SPELLINGS, because `owned` on a return reaches the model two ways. The Modifier rule
            // sits ahead of the ownership specifier and admits `owned`, so it is normally taken as a
            // modifier and lands on IsOwned; ReturnOwnership is where it arrives when it is not.
            InvocationExpression { Method.Modifiers.IsOwned: true } => true,
            InvocationExpression { Method.ReturnOwnership: OwnershipKind.Owned } => true,
            CastExpression { Operator: { } called } => Yields(called),
            ImplicitConversionExpression { Operator: { } called } => Yields(called),
            LocalReferenceExpression { Local.Ownership: OwnershipKind.Owned } => true,
            _ => false,
        };
    }

    /// <summary>
    /// A <see cref="CastOperator.CopiesClone"/> conversion is not owned: its field-by-field copy shares the
    /// operand's fields.
    /// </summary>
    public static bool Yields(CastOperator conversion)
    {
        return conversion.ClonesThrough is not null
            || conversion.Modifiers.IsOwned
            || conversion.ReturnOwnership == OwnershipKind.Owned;
    }

    /// <summary>
    /// Whether a value of the type can be copied into an independent one (ownership-v2 Requirement 8): a value
    /// type, a declared <c>Clone</c>, a conformance to IClonable, or a Mist class every field of which is
    /// clonable in turn (8.6, 8.7), a type met again while deriving counting as clonable. A generic class is
    /// derived once, its own parameters taken as clonable, and an instantiation of it also needs every
    /// argument clonable, which keeps the walk finite however its fields nest it. Broader than what
    /// <see cref="At"/> reads, because nothing synthesizes a <c>Clone</c> for a derived type and implicit
    /// cloning needs one to call.
    /// </summary>
    public static bool IsClonable(IMistType type)
    {
        return IsClonable(type, new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance));
    }

    private static bool IsClonable(IMistType type, HashSet<ConcreteType> deriving)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => IsClonable(aliased, deriving),
            ConstructedType { Definition: TypeAlias { AliasedType: { } aliased } } alias
                => IsClonable(TypeSubstitution.ForReceiver(alias).Apply(aliased, interner: null), deriving),
            RefinedType refined => IsClonable(refined.BaseType, deriving),
            UnionType union => union.Constituents.All(constituent => OptionalUnion.IsVoid(constituent) || IsClonable(constituent, deriving)),
            Newtype { SourceType: { } source } => IsClonable(source, deriving),
            TypeParameter parameter => deriving.Any(definition => Declares(definition, parameter))
                || parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Struct
                    || (constraint is TypeConstraint { ConstraintType: var bound } && IsClonable(bound, deriving))),
            StructType or ConstructedType { Definition: StructType } or EnumType or ITupleType => true,
            _ => Clonable(type) || ConformsToClonable(type) || Derives(type, deriving),
        };
    }

    private static bool ConformsToClonable(IMistType type)
    {
        var definition = type is ConstructedType constructed ? constructed.Definition : type;

        return definition.FullyQualifiedName == WellKnownTypes.IClonable.FullyQualifiedName
            || (definition is MemberType member && member.BaseAbstractTypes.Exists(ConformsToClonable))
            || (definition is ConcreteType concrete
                && (concrete.ConformancesList.Exists(conformance => ConformsToClonable(conformance.AbstractType))
                    || (concrete.BaseType is { } baseType && ConformsToClonable(baseType))));
    }

    private static bool Derives(IMistType type, HashSet<ConcreteType> deriving)
    {
        if((type is ConstructedType constructed ? constructed.Definition : type) is not ConcreteType { IsIntrinsic: false, DeclarationNode: not null } concrete
            || !Arguments(type).All(argument => IsClonable(argument, deriving)))
        {
            return false;
        }

        if(!deriving.Add(concrete))
        {
            return true;
        }

        try
        {
            return (concrete.BaseType is not { } baseType || IsClonable(baseType, deriving))
                && InstanceStorage.Of(concrete).All(slot => IsClonable(slot.Type, deriving));
        }
        finally
        {
            _ = deriving.Remove(concrete);
        }
    }

    private static IEnumerable<IMistType> Arguments(IMistType type)
    {
        for(var constructed = type as ConstructedType; constructed is not null; constructed = constructed.Enclosing)
        {
            foreach(var argument in constructed.TypeArguments)
            {
                yield return argument;
            }
        }
    }

    private static bool Declares(ConcreteType definition, TypeParameter parameter)
    {
        for(MistType? declaring = definition; declaring is not null; declaring = declaring.DeclaringType)
        {
            if(declaring is IHasTypeParameters generic && generic.TypeParametersList.Contains(parameter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type offers the clone. Read off a declared <c>Clone</c> rather than off conformance
    /// to Standard's IClonable, so a type that declares one directly is not made to name the abstract
    /// as well -- what emission needs is a member to call.
    /// </summary>
    private static bool Clonable(IMistType type)
    {
        var definition = type is ConstructedType constructed ? constructed.Definition : type;

        // AN INTRINSIC TYPE IS NOT CLONED, which is the deliberate decision the capability asked for
        // rather than an omission. Standard's String declares a Clone so it can satisfy the abstract,
        // but the member is the target's and the CLR's returns object; more to the point a copy of an
        // immutable value is indistinguishable from the value, so the clone buys no independence and
        // the aliasing it would prevent cannot be observed.
        return definition is not ConcreteType { IsIntrinsic: true }
            && (definition as MemberType)?.MethodsList.Exists(
                method => method.Name == "Clone" && method.ParametersList.Count == 0) == true
            ;
    }
}