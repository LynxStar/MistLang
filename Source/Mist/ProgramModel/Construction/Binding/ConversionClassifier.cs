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
/// its underlying type and reached from it only explicitly -- from any numeric type, for a numeric one,
/// since C# admits an explicit numeric step into an explicit operator. A newtype and its source reach
/// each other only explicitly. Those are the conversions the emitter spells as operators, stated here
/// because the model declares none. A constructed type whose argument is an unknown value of some domain
/// takes any value of that domain in the slot, so a sized array is within the unsized one; and a structural
/// function type and a CLR delegate with the same Invoke shape are each within the other, the two being
/// one type that emission alone tells apart.
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
        from = Normalize(Expanded(from, interner));
        to = Normalize(Expanded(to, interner));

        if(IsSentinel(from) || IsSentinel(to))
        {
            return ConversionKind.Unclassified;
        }

        if(Same(from, to))
        {
            return ConversionKind.Identity;
        }

        if(Within(from, to, []) || ConversionRelation.FindOperator(from, to, includeExplicit: false) is not null)
        {
            return ConversionKind.Implicit;
        }

        return ConversionRelation.FindOperator(from, to, includeExplicit: true) is not null
            || ExplicitByDeclaration(from, to)
            ? ConversionKind.Explicit
            : ConversionKind.None
            ;
    }

    private static IMistType Expanded(IMistType type, TypeInterner? interner)
    {
        return interner is null ? type : AliasTransparency.Expand(type, interner);
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

        if(to is UnionType union && union.Constituents.Any(constituent => Reaches(from, constituent, visited)))
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
            _ => Bases(from),
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

    private static bool AgreesUpToUnknownValues(IMistType from, IMistType to)
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
                && !(slot is UnknownValueType unknown && argument is LiftedType or UnknownValueType && Same(Normalize(argument), unknown.Domain))
                && !AgreesUpToUnknownValues(argument, slot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CrossesDelegateShape(IMistType from, IMistType to)
    {
        return (from is IFunctionType) != (to is IFunctionType)
            && DelegateShape.Of(from) is { } source
            && DelegateShape.Of(to) is { } target
            && DelegateShape.SameShape(source, target);
    }

    private static IEnumerable<IMistType> Bases(IMistType type)
    {
        var (declaring, substitution) = type switch
        {
            ConstructedType { Definition: MemberType definition } constructed
                => (definition, TypeSubstitution.ForReceiver(constructed)),
            MemberType member => (member, TypeSubstitution.Empty),
            _ => (null, TypeSubstitution.Empty),
        };

        if(declaring is null)
        {
            yield break;
        }

        if(declaring is ConcreteType { BaseType: { } baseType })
        {
            yield return substitution.Apply(baseType, interner: null);
        }

        foreach(var abstractBase in declaring.BaseAbstractTypes)
        {
            yield return substitution.Apply(abstractBase, interner: null);
        }

        foreach(var conformance in (declaring as ConcreteType)?.ConformancesList ?? [])
        {
            yield return substitution.Apply(conformance.AbstractType, interner: null);
        }
    }

    private static readonly string[] NumericNames =
    [
        WellKnownTypes.Byte.FullyQualifiedName, WellKnownTypes.SByte.FullyQualifiedName,
        WellKnownTypes.Int16.FullyQualifiedName, WellKnownTypes.UInt16.FullyQualifiedName,
        WellKnownTypes.Int32.FullyQualifiedName, WellKnownTypes.UInt32.FullyQualifiedName,
        WellKnownTypes.Int64.FullyQualifiedName, WellKnownTypes.UInt64.FullyQualifiedName,
        WellKnownTypes.Char.FullyQualifiedName, WellKnownTypes.Single.FullyQualifiedName,
        WellKnownTypes.Double.FullyQualifiedName, WellKnownTypes.Decimal.FullyQualifiedName,
    ];

    private static bool IsNumeric(IMistType type)
    {
        return Array.IndexOf(NumericNames, type.FullyQualifiedName) >= 0;
    }

    private static bool ExplicitByDeclaration(IMistType from, IMistType to)
    {
        return to switch
        {
            EnumType { UnderlyingType: { } underlying }
                when Reaches(from, underlying, []) || (IsNumeric(underlying) && IsNumeric(from)) => true,
            Newtype { SourceType: { } source } when Reaches(from, source, []) => true,
            RefinedType refined when Reaches(from, refined.BaseType, []) => true,
            _ => from is Newtype { SourceType: { } wrapped } && Reaches(wrapped, to, []),
        };
    }
}