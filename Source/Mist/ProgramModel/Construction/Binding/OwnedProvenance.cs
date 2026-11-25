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
/// An owned reference promises an INDEPENDENT value, so a source that something else still holds has to
/// be copied -- otherwise the two names alias and mutating either is visible through the other. An
/// owned field read through a shared holder is the same promise seen from the other side (owned-fields
/// Requirement 2.3): the holder cannot give the field away, so what it hands out is a copy.
/// </summary>
internal static class OwnedProvenance
{
    public static Expression At(CloneDerivation clones, OwnershipKind target, Expression source, bool byReference = false)
    {
        if(byReference || target is OwnershipKind.Frozen or OwnershipKind.Borrowed)
        {
            return source;
        }

        if(!Clonable(clones, source.Type))
        {
            return target == OwnershipKind.Owned && !Owns(source)
                && ResultTransfers.Discipline(source.Type) == SubstructuralDiscipline.None
                && OwnedStructCopies.Of(source.Type, clones).Required
                    ? new CloneExpression(source.Node, source, copiesOwnedFields: true)
                    : source;
        }

        return (target == OwnershipKind.Owned && !Owns(source)) || SharedExtraction(source) is not null
            ? new CloneExpression(source.Node, source)
            : source
            ;
    }

    public static IReadOnlyList<Expression> AtParameters(
        CloneDerivation clones, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        if(parameters is null)
        {
            return arguments;
        }

        var adjusted = new List<Expression>(arguments.Count);

        for(var i = 0; i < arguments.Count; i++)
        {
            adjusted.Add(i < parameters.Count
                ? At(clones, parameters[i].Modifier.Ownership, arguments[i], parameters[i].Modifier.IsRef)
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
    /// Whether the source hands over a value nothing else may still be reading.
    /// </summary>
    private static bool Owns(Expression source)
    {
        return source switch
        {
            NewExpression or CloneExpression or ArrayCreationExpression => true,

            // An EXPLICIT clone is owned provenance, so a written `x.Clone()` assigned to an owned
            // binding is not copied a second time.
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
    /// A plain <see cref="CastOperator.CopiesClone"/> conversion, not <see cref="CastOperator.CopiesDerivedClone"/>,
    /// is not owned: its field-by-field copy shares the operand's fields.
    /// </summary>
    public static bool Yields(CastOperator conversion)
    {
        return conversion.ClonesThrough is not null
            || conversion.CopiesDerivedClone
            || conversion.Modifiers.IsOwned
            || conversion.ReturnOwnership == OwnershipKind.Owned;
    }

    /// <summary>
    /// Whether the type offers the clone. Read off a declared <c>Clone</c> rather than off conformance
    /// to Standard's IClonable, so a type that declares one directly is not made to name the abstract
    /// as well -- what emission needs is a member to call.
    /// </summary>
    private static bool Clonable(CloneDerivation clones, IMistType type)
    {
        var definition = type is ConstructedType constructed ? constructed.Definition : type;

        // AN INTRINSIC TYPE IS NOT CLONED, deliberately. Standard's String declares a Clone so it can
        // satisfy the abstract, but the member is the target's and the CLR's returns object; and a copy
        // of an immutable value is indistinguishable from the value, so the clone buys no independence.
        // A derived Clone is called only where the instantiation itself derives and holds something changeable,
        // and never in place of the move a linear, affine or relevant value makes by its own discipline.
        return definition switch
        {
            ConcreteType { IsIntrinsic: true } => false,
            ConcreteType { DerivedClone: not null } => ResultTransfers.Discipline(type) == SubstructuralDiscipline.None && clones.Judge(type).Kind == CloneKind.Derived,
            MemberType member => CloneDerivation.DeclaresClone(member),
            _ => false,
        };
    }
}