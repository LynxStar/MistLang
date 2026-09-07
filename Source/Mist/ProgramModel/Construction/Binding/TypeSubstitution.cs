using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A map rather than an ordinal index: each declaration numbers its own type parameters from zero, so
/// a generic method's first shares an ordinal with its enclosing type's first, and an index would
/// answer for the method's parameter out of the receiver's arguments.
/// </summary>
internal sealed class TypeSubstitution
{
    public static readonly TypeSubstitution Empty = new([]);

    private readonly Dictionary<TypeParameter, IMistType> bindings;

    private TypeSubstitution(Dictionary<TypeParameter, IMistType> bindings)
    {
        this.bindings = bindings;
    }

    public static TypeSubstitution ForReceiver(ConstructedType? receiver)
    {
        if(receiver?.Definition is not IHasTypeParameters definition)
        {
            return Empty;
        }

        return ByOrdinal(definition.TypeParametersList, receiver.TypeArguments);
    }

    /// <summary>
    /// The receiver's own mapping composed with one per constructed base the member was reached
    /// through, which is what an INHERITED member of a generic base needs: <c>Container.T</c> is bound
    /// to <c>ExtendedContainer.T</c> by the base clause and only then to <c>int</c> by the receiver, so
    /// neither step alone answers. Each base's arguments are resolved through the map built so far
    /// before becoming bindings themselves, so the composition works to any depth and does not assume a
    /// base reuses its deriver's parameter names or order.
    /// </summary>
    public static TypeSubstitution ForMember(
        ConstructedType? receiver, LookupResult found, TypeInterner? interner)
    {
        var substitution = ForReceiver(receiver);

        foreach(var construction in ReachedThrough(found))
        {
            if(construction.Definition is not IHasTypeParameters definition)
            {
                continue;
            }

            var arguments = new IMistType?[construction.TypeArguments.Count];

            for(var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = substitution.Apply(construction.TypeArguments[i], interner);
            }

            substitution = substitution.Extend(ByOrdinal(definition.TypeParametersList, arguments));
        }

        return substitution;
    }

    private static IReadOnlyList<ConstructedType> ReachedThrough(LookupResult found)
    {
        return found switch
        {
            FieldResult field => field.BaseConstructions,
            PropertyResult property => property.BaseConstructions,
            _ => [],
        };
    }

    /// <param name="typeArguments">
    /// A null entry is skipped rather than bound, leaving its parameter for <see cref="Unbound"/>.
    /// </param>
    public static TypeSubstitution ForMethod(Method method, IReadOnlyList<IMistType?> typeArguments)
    {
        return ByOrdinal(method.TypeParametersList, typeArguments);
    }

    public static TypeSubstitution ForParameters(
        IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType?> typeArguments)
    {
        return ByOrdinal(parameters, typeArguments);
    }

    /// <summary>
    /// The ordinal indexes the arguments, not the parameter's position in
    /// <paramref name="parameters"/>. They part for a projected extension block, whose parameters keep
    /// the ordinals of the flat CLR list they were partitioned out of.
    /// </summary>
    private static TypeSubstitution ByOrdinal(
        IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType?> arguments)
    {
        var bindings = new Dictionary<TypeParameter, IMistType>();

        foreach(var parameter in parameters)
        {
            if(parameter.Ordinal < arguments.Count && arguments[parameter.Ordinal] is { } argument)
            {
                bindings[parameter] = argument;
            }
        }

        return bindings.Count == 0 ? Empty : new TypeSubstitution(bindings);
    }

    /// <summary>
    /// <see cref="ForReceiver"/> and <see cref="ForMethod"/> bind parameters of different
    /// declarations, so no key is in both maps and the last write below never loses a binding.
    /// </summary>
    public TypeSubstitution Extend(TypeSubstitution other)
    {
        if(other.bindings.Count == 0)
        {
            return this;
        }

        if(bindings.Count == 0)
        {
            return other;
        }

        var combined = new Dictionary<TypeParameter, IMistType>(bindings);

        foreach(var (parameter, argument) in other.bindings)
        {
            combined[parameter] = argument;
        }

        return new TypeSubstitution(combined);
    }

    /// <summary>
    /// An array needs no arm of its own, being a construction of <c>Standard.Array</c>. Constructions,
    /// unions, tuples and function types are descended into, so a parameter reachable solely through a
    /// refinement is the remaining case that survives untouched and unreported.
    /// </summary>
    /// <param name="interner">
    /// Null from the emitter's struct flattening, which has none: the interner belongs to construction
    /// and nothing hands it across. Without one the construction built here is equal to the interned
    /// one but not the same object, which the conversion relation does not mind, since it compares
    /// types by qualified name.
    /// </param>
    public IMistType Apply(IMistType type, TypeInterner? interner)
    {
        if(bindings.Count == 0)
        {
            return type;
        }

        if(type is TypeParameter parameter)
        {
            return bindings.TryGetValue(parameter, out var argument) ? argument : type;
        }

        if(type is SpreadType spread)
        {
            // A spread standing alone rather than inside a pack slot has nowhere to expand INTO, so
            // it substitutes its operand and stays a spread; the slot it lands in does the rest.
            var operand = Apply(spread.Operand, interner);
            return ReferenceEquals(operand, spread.Operand) ? spread : new SpreadType(operand);
        }

        if(type is LiftedType { Value: TypePackValue pack } lifted)
        {
            return ApplyToPack(lifted, pack, interner);
        }

        if(type is UnionType union)
        {
            return ApplyToUnion(union, interner);
        }

        if(type is FunctionType function)
        {
            return ApplyToFunction(function, interner);
        }

        if(type is TupleType tuple)
        {
            return ApplyToTuple(tuple, interner);
        }

        if(type is NamedTupleType namedTuple)
        {
            return ApplyToTuple(namedTuple.Underlying, interner) is TupleType closed
                && !ReferenceEquals(closed, namedTuple.Underlying)
                    ? new NamedTupleType(closed, namedTuple.ElementNames)
                    : type
                    ;
        }

        if(type is not ConstructedType constructed)
        {
            return type;
        }

        var substituted = new IMistType[constructed.TypeArguments.Count];
        var changed = false;

        for(var i = 0; i < substituted.Length; i++)
        {
            substituted[i] = Apply(constructed.TypeArguments[i], interner);
            changed |= !ReferenceEquals(substituted[i], constructed.TypeArguments[i]);
        }

        if(!changed)
        {
            return type;
        }

        return interner is null
            ? new ConstructedType(constructed.Definition, substituted)
            : interner.GetOrCreateConstructedType(constructed.Definition, substituted)
            ;
    }

    private IMistType SpliceTuple(TupleType tuple, TypeInterner? interner)
    {
        var elements = new List<IMistType>(tuple.ElementTypes.Count);
        var changed = false;

        foreach(var element in tuple.ElementTypes)
        {
            if(element is SpreadType spread
                && spread.Operand is TypeParameter parameter
                && bindings.TryGetValue(parameter, out var bound)
                && bound is LiftedType { Value: TypePackValue spliced })
            {
                elements.AddRange(spliced.Elements);
                changed = true;
                continue;
            }

            var substituted = Apply(element, interner);
            changed |= !ReferenceEquals(substituted, element);
            elements.Add(substituted);
        }

        if(!changed)
        {
            return tuple;
        }

        return interner is null ? new TupleType(elements) : interner.GetOrCreateTupleType(elements);
    }

    /// <summary>
    /// A pack's elements substituted, with a spread among them replaced by the elements of the pack
    /// it names rather than by that pack. That splice is the whole of what a spread means, and it is
    /// why a pack cannot be substituted element-for-element: one element becomes many, or none.
    /// </summary>
    private IMistType ApplyToPack(LiftedType lifted, TypePackValue pack, TypeInterner? interner)
    {
        var elements = new List<IMistType>(pack.Elements.Length);
        var changed = false;

        foreach(var element in pack.Elements)
        {
            if(element is SpreadType spread
                && spread.Operand is TypeParameter parameter
                && bindings.TryGetValue(parameter, out var bound)
                && bound is LiftedType { Value: TypePackValue spliced })
            {
                elements.AddRange(spliced.Elements);
                changed = true;
                continue;
            }

            var substituted = Apply(element, interner);
            changed |= !ReferenceEquals(substituted, element);
            elements.Add(substituted);
        }

        if(!changed)
        {
            return lifted;
        }

        var value = new TypePackValue(elements);

        return interner is null ? new LiftedType(lifted.Type, value) : interner.LiftPack(lifted.Type, value);
    }

    /// <summary>
    /// The result is substituted alongside the parameters, so an alias over a function type closes in
    /// BOTH positions -- <c>type Transform&lt;TIn, TOut&gt; = func&lt;TIn, TOut&gt;</c> being the case
    /// that needs it, where substituting only the parameters would leave the result open.
    /// </summary>
    private IMistType ApplyToFunction(FunctionType function, TypeInterner? interner)
    {
        // A spread among the parameters splices, as it does in a pack slot and in a tuple. It gets
        // here because a Func construction collapses to this type BEFORE its pack is bound, so
        // `type Action<params TArgs> = Func<TArgs..., void>` leaves the spread sitting in a
        // parameter list rather than in the argument list it was written in.
        var parameters = new List<IMistType>(function.ParameterTypes.Count);
        var changed = false;

        foreach(var parameter in function.ParameterTypes)
        {
            if(parameter is SpreadType spread
                && spread.Operand is TypeParameter packParameter
                && bindings.TryGetValue(packParameter, out var bound)
                && bound is LiftedType { Value: TypePackValue spliced })
            {
                parameters.AddRange(spliced.Elements);
                changed = true;
                continue;
            }

            var substituted = Apply(parameter, interner);
            changed |= !ReferenceEquals(substituted, parameter);
            parameters.Add(substituted);
        }

        var returnType = Apply(function.ReturnType, interner);
        changed |= !ReferenceEquals(returnType, function.ReturnType);

        if(!changed)
        {
            return function;
        }

        return interner is null
            ? new FunctionType(parameters, returnType)
            : interner.GetOrCreateFunctionType(parameters, returnType)
            ;
    }

    /// <summary>
    /// A tuple whose elements substituted, with a spread among them replaced by the elements of the
    /// pack it names -- the same splice a pack slot gets, and for the same reason: `(TArgs...)` is
    /// one element standing for however many the pack holds.
    /// </summary>
    private IMistType ApplyToTuple(TupleType tuple, TypeInterner? interner)
    {
        if(tuple.ElementTypes.Any(e => e is SpreadType))
        {
            return SpliceTuple(tuple, interner);
        }

        var elements = new IMistType[tuple.ElementTypes.Count];
        var changed = false;

        for(var i = 0; i < elements.Length; i++)
        {
            elements[i] = Apply(tuple.ElementTypes[i], interner);
            changed |= !ReferenceEquals(elements[i], tuple.ElementTypes[i]);
        }

        if(!changed)
        {
            return tuple;
        }

        return interner is null
            ? new TupleType(elements)
            : interner.GetOrCreateTupleType(elements)
            ;
    }

    private IMistType ApplyToUnion(UnionType union, TypeInterner? interner)
    {
        var substituted = new IMistType[union.Constituents.Count];
        var changed = false;

        for(var i = 0; i < substituted.Length; i++)
        {
            substituted[i] = Apply(union.Constituents[i], interner);
            changed |= !ReferenceEquals(substituted[i], union.Constituents[i]);
        }

        if(!changed)
        {
            return union;
        }

        if(interner is not null)
        {
            return interner.GetOrCreateUnionType(substituted);
        }

        var normalized = UnionType.NormalizeConstituents(substituted);

        return normalized.Count == 1 ? normalized[0] : new UnionType(normalized);
    }

    public IReadOnlyList<TypeParameter> Unbound(IReadOnlyList<TypeParameter> parameters)
    {
        List<TypeParameter>? unbound = null;

        foreach(var parameter in parameters)
        {
            if(!bindings.ContainsKey(parameter))
            {
                unbound ??= [];
                unbound.Add(parameter);
            }
        }

        return unbound ?? [];
    }
}