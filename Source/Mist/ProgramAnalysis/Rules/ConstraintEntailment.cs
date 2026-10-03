using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A type parameter forwarded into a constrained one, judged by what it DECLARES rather than by any
/// closing it stands in for: an open argument is a promise kept at every closing of the forwarding
/// declaration, so a requirement holds only where that declaration already entails it. A type
/// constraint is entailed by a declared bound that is the required type, one whose bases reach it, or
/// one that is itself a parameter whose bounds do, the walk cut at a parameter already entered and at
/// a depth no declared inheritance reaches. A keyword is entailed only by the same keyword, under the
/// lattice the closed judgment uses -- linear is affine and relevant, stampable is struct -- because a
/// type bound may be met through an implicit conversion and so promises no shape. Every constructor
/// constraint must hold of every closing, since the body may call each one, so each required signature
/// is met only by the same declared signature, or by struct where it is parameterless. Stampable
/// and default keep the direct rule <see cref="ConstraintAnalyzer.Decide"/> holds for an open
/// argument, since stampability through a bound is what its spec forbids. Types are compared by
/// identity after the closing's substitution, a parameter by reference and a construction by its
/// parts, never by a name two declarations can share.
/// </summary>
internal static class ConstraintEntailment
{
    private const int Depth = 64;

    public static DiagnosticDescriptor? Decide(TypeArgumentClosing closing, TypeParameterConstraint declared, TypeInterner interner)
    {
        if(closing.Argument is not TypeParameter offered
            || ConstraintAnalyzer.Closed(declared, closing.Closing, interner) is not { } required)
        {
            return null;
        }

        if(declared.Kind is TypeParameterConstraintKind.Stampable or TypeParameterConstraintKind.Default)
        {
            return ConstraintAnalyzer.Decide(closing, declared, interner);
        }

        if(offered.ConstraintsList.Exists(constraint => ConstraintAnalyzer.Closed(constraint, TypeSubstitution.Empty, interner) is null))
        {
            return null;
        }

        var entailed = required switch
        {
            TypeConstraint typed => Guarantees(
                offered, ConversionClassifier.Normalized(typed.ConstraintType, interner), interner, new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance), Depth),
            ConstructorConstraint constructor => GuaranteesConstructor(offered, constructor.ParameterTypes, interner),
            KeywordConstraint keyword => GuaranteesKeyword(offered, keyword.Kind),
            _ => true,
        };

        if(entailed)
        {
            return null;
        }

        return required is ConstructorConstraint
            ? ProgramAnalysisDiagnostics.ConstructorConstraintNotSatisfied
            : ProgramAnalysisDiagnostics.ConstraintNotSatisfied;
    }

    private static bool Guarantees(TypeParameter offered, IMistType required, TypeInterner interner, HashSet<TypeParameter> entered, int depth)
    {
        if(required is UnknownType)
        {
            return true;
        }

        if(!entered.Add(offered))
        {
            return false;
        }

        return offered.ConstraintsList.OfType<TypeConstraint>()
            .Any(bound => Reaches(bound.ConstraintType, required, interner, entered, depth));
    }

    private static bool Reaches(IMistType offered, IMistType required, TypeInterner interner, HashSet<TypeParameter> entered, int depth)
    {
        offered = ConversionClassifier.Normalized(offered, interner);

        if(Same(offered, required))
        {
            return true;
        }

        if(depth == 0)
        {
            return false;
        }

        return offered switch
        {
            TypeParameter bound => Guarantees(bound, required, interner, entered, depth - 1),
            RefinedType refined => Reaches(refined.BaseType, required, interner, entered, depth - 1),
            _ => ConversionRelation.Bases(offered).Any(baseType => Reaches(baseType, required, interner, entered, depth - 1)),
        };
    }

    private static bool GuaranteesConstructor(TypeParameter offered, IReadOnlyList<IMistType> required, TypeInterner interner)
    {
        return (required.Count == 0 && Declares(offered, TypeParameterConstraintKind.Struct))
            || offered.ConstraintsList.OfType<ConstructorConstraint>().Any(offer => SameSignature(offer.ParameterTypes, required, interner));
    }

    private static bool GuaranteesKeyword(TypeParameter offered, TypeParameterConstraintKind required)
    {
        return Declares(offered, required) || required switch
        {
            TypeParameterConstraintKind.Struct => Declares(offered, TypeParameterConstraintKind.Stampable),
            TypeParameterConstraintKind.Affine or TypeParameterConstraintKind.Relevant => Declares(offered, TypeParameterConstraintKind.Linear),
            _ => false,
        };
    }

    private static bool Declares(TypeParameter parameter, TypeParameterConstraintKind kind)
    {
        return parameter.ConstraintsList.Exists(constraint => constraint is KeywordConstraint && constraint.Kind == kind);
    }

    private static bool SameSignature(IReadOnlyList<IMistType> offer, IReadOnlyList<IMistType> required, TypeInterner interner)
    {
        return offer.Count == required.Count
            && offer.Zip(required).All(pair => Same(
                ConversionClassifier.Normalized(pair.First, interner), ConversionClassifier.Normalized(pair.Second, interner)));
    }

    private static bool Same(IMistType left, IMistType right)
    {
        if(ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (INamedTupleType named, _) => Same(named.Underlying, right),
            (_, INamedTupleType named) => Same(left, named.Underlying),
            (PackElementType first, PackElementType second) => ReferenceEquals(first.Pack, second.Pack) && first.Index == second.Index,
            (TypeParameter, _) or (_, TypeParameter) => false,
            (UnknownType, UnknownType) => true,
            (ConstructedType first, ConstructedType second) => Same(first.Definition, second.Definition)
                && SameAll(first.TypeArguments, second.TypeArguments)
                && SameEnclosing(first.Enclosing, second.Enclosing),
            (ITupleType first, ITupleType second) => SameAll(first.ElementTypes, second.ElementTypes),
            (UnionType first, UnionType second) => first.Constituents.Count == second.Constituents.Count
                && first.Constituents.All(constituent => second.Constituents.Any(other => Same(constituent, other))),
            (FunctionType first, FunctionType second) => first.IsPure == second.IsPure
                && Same(first.ReturnType, second.ReturnType)
                && SameAll(first.ParameterTypes, second.ParameterTypes),
            (SpreadType first, SpreadType second) => Same(first.Operand, second.Operand),
            (LiftedType first, LiftedType second) => Same(first.Type, second.Type) && SameValue(first.Value, second.Value),
            (RefinedType first, RefinedType second) => ReferenceEquals(first.Declaration, second.Declaration) && Same(first.BaseType, second.BaseType),
            (ConstructedType or ITupleType or UnionType or FunctionType or SpreadType or LiftedType or RefinedType, _) => false,
            (_, ConstructedType or ITupleType or UnionType or FunctionType or SpreadType or LiftedType or RefinedType) => false,
            _ => ConversionRelation.IsIdentity(left, right),
        };
    }

    private static bool SameEnclosing(ConstructedType? left, ConstructedType? right)
    {
        return left is null || right is null ? left is null && right is null : Same(left, right);
    }

    private static bool SameValue(object left, object right)
    {
        return left is TypePackValue first && right is TypePackValue second
            ? SameAll(first.Elements, second.Elements)
            : Equals(left, right);
    }

    private static bool SameAll(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));
    }
}