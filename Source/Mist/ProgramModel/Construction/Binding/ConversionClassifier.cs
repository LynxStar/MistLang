using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal enum ConversionKind
{
    Identity,
    Implicit,
    Explicit,
    None,
    Unclassified,
}

/// <summary>
/// Which conversion takes a value of one type to another, answered for every pair so that a rule may
/// REFUSE on it. The sibling of <see cref="ConversionRelation"/> rather than its replacement: overload
/// selection needs a relation that narrows and ranks without refusing, and the kinds that relation
/// declines are exactly the rules stated here.
/// <para>
/// A constructed type reaches the bases of its definition with its own arguments substituted in, so
/// <c>Box&lt;int&gt;</c> is within <c>IBox&lt;int&gt;</c> and not within <c>IBox&lt;string&gt;</c>; there is no
/// variance beyond the covariance to <c>unknown</c> the relation already states. A type parameter is
/// within its own type constraints and nothing below them. A refinement is within its base, and the
/// base reaches it only explicitly, its predicate being the refinement prover's to discharge rather than
/// this classification's. A dependent value classifies as the type it is a value of. An enum is within
/// its underlying type, and is reached explicitly from whatever reaches that type implicitly, a cast to it
/// being a call whose argument is the underlying value (see <see cref="Model.Expressions.CastExpression"/>).
/// A newtype and its source reach each other only explicitly. Those are the conversions the emitter
/// spells as operators, stated here because the model declares none. A constructed type whose argument is
/// an unknown value of some domain takes any value of that domain in the slot, so a sized array is within
/// the unsized one; and a structural function type and a CLR delegate with the same Invoke shape are each
/// within the other, the two being one type that emission alone tells apart.
/// </para>
/// <para>
/// UNCLASSIFIED is not a fifth kind of conversion. It answers for a sentinel standing in for a failure or
/// a gap -- an error type, a CLR type the projection skipped -- where there is no type to classify, and a
/// rule refusing on it would be complaining about the compiler rather than the program.
/// </para>
/// </summary>
internal static class ConversionClassifier
{
    public static ConversionKind Classify(IMistType from, IMistType to, TypeInterner? interner = null)
    {
        from = Normalized(from, interner);
        to = Normalized(to, interner);

        var kind = ImplicitKind(from, to, interner);

        return kind == ConversionKind.None && (CallsAnOperator(from, to, interner) || ExplicitByDeclaration(from, to))
            ? ConversionKind.Explicit
            : kind
            ;
    }

    public static bool IsImplicit(IMistType from, IMistType to, TypeInterner? interner)
    {
        return ImplicitKind(Normalized(from, interner), Normalized(to, interner), interner) != ConversionKind.None;
    }

    public static bool IsFree(IMistType from, IMistType to, TypeInterner? interner)
    {
        from = Normalized(from, interner);
        to = Normalized(to, interner);

        return !IsSentinel(from) && !IsSentinel(to) && (Same(from, to) || Within(from, to, []));
    }

    public static IMistType Normalized(IMistType type, TypeInterner? interner)
    {
        return Normalize(interner is null ? type : AliasTransparency.Expand(type, interner));
    }

    private static ConversionKind ImplicitKind(IMistType from, IMistType to, TypeInterner? interner)
    {
        if(IsSentinel(from) || IsSentinel(to))
        {
            return ConversionKind.Unclassified;
        }

        if(Same(from, to))
        {
            return ConversionKind.Identity;
        }

        return Within(from, to, []) || CastOperatorSelection.Implicit(from, to, interner).Count > 0
            ? ConversionKind.Implicit
            : ConversionKind.None
            ;
    }

    private static bool CallsAnOperator(IMistType from, IMistType to, TypeInterner? interner)
    {
        return CastOperatorSelection.Best(from, to, interner) is [var called]
            && CastOperatorSelection.ReachesAsArgument(from, called.Parameter, interner);
    }

    private static IMistType Normalize(IMistType type)
    {
        return type switch
        {
            INamedTupleType named => named.Underlying,
            TypeAlias { AliasedType: { } aliased } => Normalize(aliased),
            LiftedType lifted => Normalize(lifted.Type),
            UnknownValueType value => Normalize(value.Domain),
            _ => type,
        };
    }

    private static bool IsSentinel(IMistType type)
    {
        return type is ErrorType or NotProjectedType or MethodGroupType;
    }

    private static bool Same(IMistType left, IMistType right)
    {
        return ReferenceEquals(left, right) || ConversionRelation.IsIdentity(left, right);
    }

    private static bool Within(IMistType from, IMistType to, HashSet<string> visited)
    {
        if(ConversionRelation.IsWithin(from, to) || AgreesUpToUnknownValues(from, to) || CrossesDelegateShape(from, to))
        {
            return true;
        }

        if(to is UnionType union
            && ConversionRelation.CloneSourceOf(from) is null
            && union.Constituents.Any(constituent => Reaches(from, constituent, visited)))
        {
            return true;
        }

        var wider = from switch
        {
            TypeParameter parameter => parameter.ConstraintsList.OfType<TypeConstraint>()
                .Select(constraint => constraint.ConstraintType)
                .Concat(parameter.Domain is { } domain ? new IMistType[] { domain } : []),
            RefinedType refined => [refined.BaseType],
            EnumType { UnderlyingType: { } underlying } => [underlying],
            _ => ConversionRelation.Bases(from),
        };

        foreach(var candidate in wider)
        {
            if(visited.Add(candidate.FullyQualifiedName) && Reaches(candidate, to, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Reaches(IMistType from, IMistType to, HashSet<string> visited)
    {
        from = Normalize(from);

        return Same(from, to) || Within(from, to, visited);
    }

    public static bool AgreesUpToUnknownValues(IMistType from, IMistType to)
    {
        if(from is not ConstructedType source
            || to is not ConstructedType target
            || !Same(source.Definition, target.Definition)
            || source.TypeArguments.Count != target.TypeArguments.Count)
        {
            return false;
        }

        for(var i = 0; i < source.TypeArguments.Count; i++)
        {
            var argument = source.TypeArguments[i];
            var slot = target.TypeArguments[i];

            if(!Same(argument, slot)
                && !ErasesDependentValue(argument, slot, target.Definition, i)
                && !AgreesUpToUnknownValues(argument, slot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ErasesDependentValue(IMistType argument, IMistType slot, IMistType definition, int ordinal)
    {
        if(argument is not (LiftedType or UnknownValueType))
        {
            return false;
        }

        if(slot is UnknownValueType unknown)
        {
            return Same(Normalize(argument), unknown.Domain);
        }

        if(slot is not UnknownType || definition is not IHasTypeParameters declared)
        {
            return false;
        }

        var domain = declared.TypeParametersList
            .FirstOrDefault(parameter => parameter.Ordinal == ordinal && parameter.IsDependent)?.Domain;

        return domain is not null
            && Same(Normalize(argument), Normalize(ConversionRelation.Unrefined(domain)));
    }

    private static bool CrossesDelegateShape(IMistType from, IMistType to)
    {
        return (from is IFunctionType) != (to is IFunctionType)
            && to is not IFunctionType { IsPure: true }
            && DelegateShape.Of(from) is { } source
            && DelegateShape.Of(to) is { } target
            && DelegateShape.SameShape(source, target);
    }

    private static bool ExplicitByDeclaration(IMistType from, IMistType to)
    {
        return to switch
        {
            EnumType { UnderlyingType: { } underlying }
                when Classify(from, underlying) is ConversionKind.Identity or ConversionKind.Implicit => true,
            Newtype { SourceType: { } source } when Reaches(from, source, []) => true,
            RefinedType refined when Reaches(from, refined.BaseType, []) => true,
            _ => from is Newtype { SourceType: { } wrapped } && Reaches(wrapped, to, []),
        };
    }
}