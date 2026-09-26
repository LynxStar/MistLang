using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

internal static class ContractImplication
{
    public static bool Proves(IMistType source, IMistType target, Admitted? lifted = null)
    {
        return !HasRefinement(source) && !HasRefinement(target) || ProvesRefinement(source, target, lifted);
    }

    private static bool ProvesRefinement(IMistType source, IMistType target, Admitted? lifted = null)
    {
        source = Unalias(source);
        target = Unalias(target);

        if(ReferenceEquals(source, target))
        {
            return true;
        }

        if(source is UnionType produced)
        {
            return produced.Constituents.All(part => ProvesRefinement(part, target));
        }

        if(target is UnionType promised)
        {
            return promised.Constituents.Any(part => ProvesRefinement(source, part, lifted));
        }

        if(target is RefinedType refined)
        {
            if(LiteralPredicates.IsRefused(refined) || Contains(source, refined))
            {
                return true;
            }

            if(!SameBase(source, target))
            {
                return false;
            }

            if(RealRequirement.KindOf(target) is { } kind)
            {
                return RealRequirement.OfType(target) is { } demanded && RealRequirement.OfType(source)?.ConvertedTo(kind) is { } supplied
                    && supplied.IsSubsetOf(demanded);
            }

            return Requirement.OfType(target) is { Bounds.Length: 0 } required
                && !LiteralPredicates.Applies(target)
                && (Requirement.ValuesOf(source) ?? Admitted.Everything).And(lifted ?? Admitted.Everything).IsSubsetOf(required.Values)
                && ProvesRefinement(Requirement.Unrefined(source), Requirement.Unrefined(target));
        }

        source = Requirement.Unrefined(source);

        if(!HasRefinement(source) && !HasRefinement(target))
        {
            return source is not TypeParameter and not UnknownType and not UnknownValueType
                && target is not TypeParameter and not UnknownType and not UnknownValueType
                && ConversionClassifier.Classify(source, target) is ConversionKind.Identity or ConversionKind.Implicit;
        }

        return (source, target) switch
        {
            (ConstructedType left, ConstructedType right) =>
                left.Definition.FullyQualifiedName == right.Definition.FullyQualifiedName
                && left.TypeArguments.Count == right.TypeArguments.Count
                && left.TypeArguments.Zip(right.TypeArguments).All(pair => InvariantRefinement(pair.First, pair.Second))
                && Enclosing(left.Enclosing, right.Enclosing),
            (INamedTupleType left, _) => ProvesRefinement(left.Underlying, target),
            (_, INamedTupleType right) => ProvesRefinement(source, right.Underlying),
            (TupleType left, TupleType right) => left.ElementTypes.Count == right.ElementTypes.Count
                && left.ElementTypes.Zip(right.ElementTypes).All(pair => InvariantRefinement(pair.First, pair.Second)),
            (FunctionType left, FunctionType right) => left.ParameterTypes.Count == right.ParameterTypes.Count
                && left.ParameterTypes.Zip(right.ParameterTypes).All(pair => InvariantRefinement(pair.First, pair.Second))
                && InvariantRefinement(left.ReturnType, right.ReturnType),
            _ => false,
        };
    }

    public static bool Invariant(IMistType source, IMistType target)
    {
        return Proves(source, target) && Proves(target, source);
    }

    private static bool InvariantRefinement(IMistType source, IMistType target)
    {
        return ProvesRefinement(source, target) && ProvesRefinement(target, source);
    }

    private static bool Enclosing(ConstructedType? source, ConstructedType? target)
    {
        return source is null ? target is null : target is not null && InvariantRefinement(source, target);
    }

    private static bool SameBase(IMistType source, IMistType target)
    {
        var left = Requirement.Unrefined(source);
        var right = Requirement.Unrefined(target);
        return (left is LiftedType lifted ? lifted.Type : left).FullyQualifiedName == right.FullyQualifiedName;
    }

    private static bool Contains(IMistType source, RefinedType target)
    {
        return source switch
        {
            TypeAlias { AliasedType: { } aliased } => Contains(aliased, target),
            RefinedType refined =>
                (ReferenceEquals(refined.Declaration, target.Declaration)
                    && ConversionClassifier.Classify(Requirement.Unrefined(refined), Requirement.Unrefined(target)) == ConversionKind.Identity
                    && InvariantRefinement(refined.BaseType, target.BaseType))
                || Contains(refined.BaseType, target),
            _ => false,
        };
    }

    private static bool HasRefinement(IMistType type)
    {
        return type switch
        {
            RefinedType => true,
            TypeAlias { AliasedType: { } aliased } => HasRefinement(aliased),
            ConstructedType constructed => constructed.TypeArguments.Any(HasRefinement)
                || (constructed.Enclosing is { } enclosing && HasRefinement(enclosing)),
            INamedTupleType named => HasRefinement(named.Underlying),
            TupleType tuple => tuple.ElementTypes.Any(HasRefinement),
            FunctionType function => function.ParameterTypes.Any(HasRefinement) || HasRefinement(function.ReturnType),
            UnionType union => union.Constituents.Any(HasRefinement),
            _ => false,
        };
    }

    private static IMistType Unalias(IMistType type)
    {
        return type is TypeAlias { AliasedType: { } aliased } ? Unalias(aliased) : type;
    }
}
