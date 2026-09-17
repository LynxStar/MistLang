using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a call's arguments say about a generic method's own type parameters, read off by matching each
/// declared parameter type against the argument standing in that slot.
/// </summary>
internal static class TypeArgumentInference
{
    /// <summary>
    /// One slot per type parameter the method declares, by ordinal -- not one per argument.
    /// </summary>
    public static IMistType?[] Infer(
        IOverload method,
        IReadOnlyList<Expression> arguments,
        TypeSubstitution receiver,
        TypeInterner interner,
        out TypeArgumentConflict? conflict)
    {
        var bounds = NewBounds(method.TypeParametersList.Count);

        if(ValuePack.FlattenedSlot(method.ParametersList) is { } flattened)
        {
            InferAcrossPack(method, arguments, receiver, interner, bounds, flattened);
            return Fix(method.TypeParametersList, bounds, interner, out conflict);
        }

        // OverloadResolution.Select has already matched the counts exactly, so a mismatch here is
        // not this method's to reject.
        var slots = Math.Min(method.ParametersList.Count, arguments.Count);

        for(var i = 0; i < slots; i++)
        {
            Match(receiver.Apply(method.ParametersList[i].Type, interner), arguments[i].Type,
                method.TypeParametersList, bounds);
        }

        return Fix(method.TypeParametersList, bounds, interner, out conflict);
    }

    /// <summary>
    /// The type arguments a DECLARED shape takes to become an actual one, or null when some
    /// parameter stayed open. Used where the only thing to unify against is a single type rather
    /// than an argument list -- an extension block's receiver, which is all a generic extension has
    /// to learn its parameters from.
    /// </summary>
    public static IReadOnlyList<IMistType>? Unify(
        IMistType declared, IMistType actual, IReadOnlyList<TypeParameter> parameters)
    {
        var bounds = NewBounds(parameters.Count);
        Match(declared, actual, parameters, bounds);

        var closed = new IMistType[bounds.Length];

        for(var i = 0; i < bounds.Length; i++)
        {
            if(bounds[i] is not [var slot])
            {
                return null;
            }

            closed[i] = slot;
        }

        return closed;
    }

    /// <summary>
    /// The fixed parameters are matched against the arguments at their own ends, and the pack is
    /// bound to the types of everything between -- which is the one inference a positional walk
    /// cannot do, because the slot and the argument stop sharing an index at the pack.
    /// </summary>
    private static void InferAcrossPack(
        IOverload method,
        IReadOnlyList<Expression> arguments,
        TypeSubstitution receiver,
        TypeInterner interner,
        List<IMistType>[] bounds,
        int flattened)
    {
        var parameters = method.ParametersList;
        var trailing = parameters.Count - flattened - 1;

        for(var i = 0; i < flattened && i < arguments.Count; i++)
        {
            Match(receiver.Apply(parameters[i].Type, interner), arguments[i].Type,
                method.TypeParametersList, bounds);
        }

        for(var i = 0; i < trailing; i++)
        {
            var parameter = parameters[parameters.Count - 1 - i];
            var argument = arguments[arguments.Count - 1 - i];
            Match(receiver.Apply(parameter.Type, interner), argument.Type,
                method.TypeParametersList, bounds);
        }

        if(ValuePack.PackParameterOf(parameters[flattened]) is not { } pack)
        {
            return;
        }

        var absorbed = ValuePack.Absorbed(
            parameters, [.. arguments.Select(a => a.Type)], flattened);

        var domain = pack.Domain ?? pack;
        Record(pack, interner.LiftPack(domain, new TypePackValue(absorbed)),
            method.TypeParametersList, bounds);
    }

    /// <summary>
    /// The two constructions must share a definition, so an argument that reaches the declared type
    /// only through one of its own bases infers nothing.
    /// </summary>
    private static void Match(
        IMistType declared, IMistType actual, IReadOnlyList<TypeParameter> parameters, List<IMistType>[] bounds)
    {
        if(declared is TypeParameter parameter)
        {
            Record(parameter, actual, parameters, bounds);
            return;
        }

        // A refinement stands for its base here, as it does under substitution: a `where` clause is
        // the only mention of T in `Ne<T>(T key) where key not 18`, and the predicate says nothing
        // about which type argument to learn.
        if(declared is RefinedType refined)
        {
            Match(refined.BaseType, actual, parameters, bounds);
            return;
        }

        // A function argument carries bindings in both directions: its RESULT names a parameter that
        // nothing else in the signature mentions -- `Apply(Func<TArgs..., TResult> f, params TArgs)`
        // has no other way to learn TResult -- and its own parameters match position for position
        // once a spread among them is skipped, that part being the pack binding's to make.
        if(declared is FunctionType declaredFunction && actual is FunctionType actualFunction)
        {
            Match(declaredFunction.ReturnType, actualFunction.ReturnType, parameters, bounds);

            if(declaredFunction.ParameterTypes.Count == actualFunction.ParameterTypes.Count)
            {
                for(var i = 0; i < declaredFunction.ParameterTypes.Count; i++)
                {
                    Match(declaredFunction.ParameterTypes[i], actualFunction.ParameterTypes[i], parameters, bounds);
                }
            }

            return;
        }

        if(declared is not ConstructedType declaredConstructed)
        {
            return;
        }

        // The argument's own construction, or the one its base chain reaches: `F<T>(Holder<T>)`
        // called with an IntHolder that conforms to Holder<int> learns T from that base.
        if(Construction(actual, declaredConstructed.Definition) is not { } actualConstructed
            || declaredConstructed.TypeArguments.Count != actualConstructed.TypeArguments.Count)
        {
            return;
        }

        for(var i = 0; i < declaredConstructed.TypeArguments.Count; i++)
        {
            Match(declaredConstructed.TypeArguments[i], actualConstructed.TypeArguments[i], parameters, bounds);
        }
    }

    /// <summary>
    /// A parameter the method does not declare is the receiver's to close, not this call's, so it is
    /// passed over rather than refused -- including where the receiver was not constructed and so left
    /// it open.
    /// </summary>
    private static ConstructedType? Construction(IMistType actual, IMistType definition)
    {
        var pending = new Queue<IMistType>();
        pending.Enqueue(actual);

        while(pending.Count > 0)
        {
            var current = pending.Dequeue();

            if(current is ConstructedType constructed
                && string.Equals(
                    constructed.Definition.FullyQualifiedName, definition.FullyQualifiedName, StringComparison.Ordinal))
            {
                return constructed;
            }

            foreach(var baseType in ConversionRelation.Bases(current))
            {
                pending.Enqueue(baseType);
            }
        }

        return null;
    }

    private static List<IMistType>[] NewBounds(int count)
    {
        var bounds = new List<IMistType>[count];

        for(var i = 0; i < count; i++)
        {
            bounds[i] = [];
        }

        return bounds;
    }

    private static void Record(
        TypeParameter parameter, IMistType actual, IReadOnlyList<TypeParameter> parameters, List<IMistType>[] bounds)
    {
        if(!parameters.Contains(parameter) || parameter.Ordinal >= bounds.Length)
        {
            return;
        }

        var slot = bounds[parameter.Ordinal];

        if(!slot.Exists(bound => ReferenceEquals(bound, actual) || ConversionRelation.IsIdentity(bound, actual)))
        {
            slot.Add(actual);
        }
    }

    private static IMistType?[] Fix(
        IReadOnlyList<TypeParameter> parameters,
        List<IMistType>[] bounds,
        TypeInterner interner,
        out TypeArgumentConflict? conflict)
    {
        var fixedTypes = new IMistType?[bounds.Length];
        conflict = null;

        for(var i = 0; i < bounds.Length; i++)
        {
            if(bounds[i].Count == 0)
            {
                continue;
            }

            fixedTypes[i] = Dominant(bounds[i], interner);

            if(fixedTypes[i] is null)
            {
                conflict ??= new TypeArgumentConflict(parameters[i], bounds[i]);
            }
        }

        if(Array.Exists(bounds, slot => slot.Count == 0))
        {
            conflict = null;
        }

        return fixedTypes;
    }

    /// <summary>
    /// Judged by <see cref="ConversionClassifier"/>, not the relation selection ranks on: no answer here is a
    /// refusal, so it must rest on the classification Stage 5 refuses an argument on. A refined bound the rest
    /// reach only through its base is taken when no bound is reached freely, the predicate being the prover's
    /// to discharge. A common base or a union of the bounds is never invented; the caller states one
    /// (type-argument-inference Requirement 3).
    /// </summary>
    private static IMistType? Dominant(List<IMistType> bounds, TypeInterner interner)
    {
        return bounds.Find(candidate => bounds.TrueForAll(bound => ConversionClassifier.IsImplicit(bound, candidate, interner)))
            ?? bounds.Find(candidate => bounds.TrueForAll(
                bound => ConversionClassifier.IsImplicit(bound, ConversionRelation.Unrefined(candidate), interner)))
            ;
    }
}