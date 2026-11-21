using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Forwarding promises every closing satisfies the required constraints, so only declared bounds count.
/// A type bound may hold through a conversion and promises no keyword shape; every constructor signature
/// must hold because the body may call each one. Stampable and default use <see cref="ConstraintAnalyzer.Decide"/>;
/// stampability cannot follow through a bound. Compare substituted types structurally, with parameter
/// identity, because distinct declarations may share a name. Each search path visits a declaration
/// once, so cyclic bases terminate even when substitution keeps growing their type arguments.
/// </summary>
internal static class ConstraintEntailment
{
    public static MistDiagnostic? Decide(TypeArgumentClosing closing, TypeParameterConstraint declared, TypeInterner interner)
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
                offered, ConversionClassifier.Normalized(typed.ConstraintType, interner), interner),
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

    private static bool Guarantees(TypeParameter offered, IMistType required, TypeInterner interner)
    {
        if(required is UnknownType)
        {
            return true;
        }

        var active = new HashSet<IMistType>(ReferenceEqualityComparer.Instance) { offered };
        var pending = new Stack<(IMistType Type, bool Leaving)>();
        foreach(var bound in Offered(offered, interner).OfType<TypeConstraint>())
        {
            pending.Push((bound.ConstraintType, false));
        }

        while(pending.TryPop(out var step))
        {
            if(step.Leaving)
            {
                _ = active.Remove(step.Type);
                continue;
            }

            var current = ConversionClassifier.Normalized(step.Type, interner);
            if(Same(current, required))
            {
                return true;
            }

            var declaration = current is ConstructedType constructed ? constructed.Definition : current;
            if(!active.Add(declaration))
            {
                continue;
            }

            pending.Push((declaration, true));
            var bounds = current switch
            {
                TypeParameter parameter => Offered(parameter, interner).OfType<TypeConstraint>().Select(bound => bound.ConstraintType),
                RefinedType refined => [refined.BaseType],
                _ => ConversionRelation.Bases(current),
            };

            foreach(var bound in bounds)
            {
                pending.Push((bound, false));
            }
        }

        return false;
    }

    private static IEnumerable<TypeParameterConstraint> Offered(TypeParameter offered, TypeInterner interner)
    {
        var element = offered is PackElementType projected ? TypeSubstitution.Empty.With(projected.Pack, projected) : TypeSubstitution.Empty;

        return offered.ConstraintsList.Select(constraint => ConstraintAnalyzer.Closed(constraint, element, interner)).OfType<TypeParameterConstraint>();
    }

    private static bool GuaranteesConstructor(TypeParameter offered, IReadOnlyList<IMistType> required, TypeInterner interner)
    {
        return (required.Count == 0 && Declares(offered, TypeParameterConstraintKind.Struct))
            || Offered(offered, interner).OfType<ConstructorConstraint>().Any(offer => SameSignature(offer.ParameterTypes, required, interner));
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