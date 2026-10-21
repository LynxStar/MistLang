using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

internal static class ContractImplication
{
    public static bool Proves(IMistType source, IMistType target, Admitted? lifted = null, SymbolCorrespondence? symbols = null)
    {
        return (!HasRefinement(source) && !HasRefinement(target)) || ProvesRefinement(source, target, symbols ?? SymbolCorrespondence.None, lifted);
    }

    private static bool ProvesRefinement(IMistType source, IMistType target, SymbolCorrespondence symbols, Admitted? lifted = null)
    {
        source = Unalias(source);
        target = Unalias(target);

        if(ReferenceEquals(source, target))
        {
            return true;
        }

        if(source is UnionType produced)
        {
            return produced.Constituents.All(part => ProvesRefinement(part, target, symbols));
        }

        if(target is UnionType promised)
        {
            return promised.Constituents.Any(part => ProvesRefinement(source, part, symbols, lifted));
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

            return Requirement.OfType(target) is { } required
                && !LiteralPredicates.Applies(target)
                && Implies(source, (Requirement.ValuesOf(source) ?? Admitted.Everything).And(lifted ?? Admitted.Everything), target, required, symbols)
                && ProvesRefinement(Requirement.Unrefined(source), Requirement.Unrefined(target), symbols);
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
                && left.TypeArguments.Zip(right.TypeArguments).All(pair => InvariantRefinement(pair.First, pair.Second, symbols))
                && Enclosing(left.Enclosing, right.Enclosing, symbols),
            (INamedTupleType left, _) => ProvesRefinement(left.Underlying, target, symbols),
            (_, INamedTupleType right) => ProvesRefinement(source, right.Underlying, symbols),
            (TupleType left, TupleType right) => left.ElementTypes.Count == right.ElementTypes.Count
                && left.ElementTypes.Zip(right.ElementTypes).All(pair => InvariantRefinement(pair.First, pair.Second, symbols)),
            (FunctionType left, FunctionType right) => left.ParameterTypes.Count == right.ParameterTypes.Count
                && left.ParameterTypes.Zip(right.ParameterTypes).All(pair => InvariantRefinement(pair.First, pair.Second, symbols))
                && InvariantRefinement(left.ReturnType, right.ReturnType, symbols),
            _ => false,
        };
    }

    public static bool Invariant(IMistType source, IMistType target, SymbolCorrespondence? symbols = null)
    {
        return Proves(source, target, symbols: symbols) && Proves(target, source, symbols: symbols);
    }

    private static bool InvariantRefinement(IMistType source, IMistType target, SymbolCorrespondence symbols)
    {
        return ProvesRefinement(source, target, symbols) && ProvesRefinement(target, source, symbols);
    }

    private static bool Enclosing(ConstructedType? source, ConstructedType? target, SymbolCorrespondence symbols)
    {
        return source is null ? target is null : target is not null && InvariantRefinement(source, target, symbols);
    }

    // A conjunction of intervals and named bounds implies another when its values fit and every bound demanded
    // is supplied over the same symbol, as strictly or more: no arithmetic relates two symbols (refinement-types
    // Requirement 13.4), so `< Count` never meets `<= Count - 1`, and a domain no value inhabits implies anything.
    private static bool Implies(IMistType source, Admitted values, IMistType target, Requirement required, SymbolCorrespondence symbols)
    {
        if(values.IsSubsetOf(Admitted.Nothing))
        {
            return true;
        }

        var supplied = Requirement.OfType(source)?.Bounds ?? [];

        return values.IsSubsetOf(required.Values)
            && required.Bounds.All(demanded => supplied.Any(bound => Stricter(bound.Kind, demanded.Kind)
                && SameSymbol(bound, source, demanded, target, symbols)));
    }

    private static bool Stricter(ComparisonKind supplied, ComparisonKind demanded)
    {
        return supplied == demanded
            || (supplied, demanded) is (ComparisonKind.LessThan, ComparisonKind.LessThanOrEqual) or (ComparisonKind.GreaterThan, ComparisonKind.GreaterThanOrEqual);
    }

    private static bool SameSymbol(SymbolicBound supplied, IMistType source, SymbolicBound demanded, IMistType target, SymbolCorrespondence symbols)
    {
        return (supplied, demanded) switch
        {
            ({ Member: { } left, Receiver: null }, { Member: { } right, Receiver: null }) => symbols.SameMember(left, right),
            ({ Member: { } left, Receiver: { } through }, { Member: { } right, Receiver: { } other }) =>
                ReferenceEquals(left, right) && symbols.SameReceiver(through, other),
            ({ Member: null, Named: TypeParameter { IsDependent: true } left }, { Member: null, Named: TypeParameter right }) =>
                ReferenceEquals(left, right) && !Closes(source, left) && !Closes(target, right),
            ({ Member: null, Named: Parameter left }, { Member: null, Named: Parameter right }) => symbols.SameParameter(left, right),
            _ => false,
        };
    }

    private static bool Closes(IMistType type, TypeParameter parameter)
    {
        return type switch
        {
            RefinedType refined => refined.Closing.ContainsKey(parameter) || Closes(refined.BaseType, parameter),
            TypeAlias { AliasedType: { } aliased } => Closes(aliased, parameter),
            _ => false,
        };
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
                    && SameClosing(refined.Closing, target.Closing)
                    && ConversionClassifier.Classify(Requirement.Unrefined(refined), Requirement.Unrefined(target)) == ConversionKind.Identity
                    && InvariantRefinement(refined.BaseType, target.BaseType, SymbolCorrespondence.None))
                || Contains(refined.BaseType, target),
            _ => false,
        };
    }

    private static bool SameClosing(IReadOnlyDictionary<TypeParameter, IMistType> source, IReadOnlyDictionary<TypeParameter, IMistType> target)
    {
        return source.Count == target.Count && source.All(pair => target.TryGetValue(pair.Key, out var other)
            && (ReferenceEquals(pair.Value, other)
                || (Requirement.ValuesOf(pair.Value) is { } supplied && Lengths.Exact(supplied) is not null && supplied.Equals(Requirement.ValuesOf(other)))));
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