using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Where an owned position gets its value, and what to do when the source does not already own one.
/// An owned binding promises an INDEPENDENT value, so a source that something else still holds has to
/// be copied -- otherwise the two names alias and mutating either is visible through the other, which
/// is the whole of what owning is meant to rule out.
/// </summary>
internal static class OwnedProvenance
{
    /// <summary>
    /// The expression to bind at an owned position: the source itself where it already owns its value,
    /// and a clone of it otherwise. A type with no <c>Clone</c> is left alone -- whether owning it is
    /// legal at all is a judgement on a finished model, and refusing here would turn a rule about
    /// provenance into one about which types may be owned.
    /// </summary>
    public static Expression At(OwnershipKind target, Expression source)
    {
        return target == OwnershipKind.Owned && !Owns(source) && Clonable(source.Type)
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
                ? At(parameters[i].Modifier.Ownership, arguments[i])
                : arguments[i]);
        }

        return adjusted;
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