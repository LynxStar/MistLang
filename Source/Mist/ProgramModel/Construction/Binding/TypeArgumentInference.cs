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
        ILambdaTrials? lambdas,
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
        var callables = new List<int>();

        for(var i = 0; i < slots; i++)
        {
            var declared = receiver.Apply(method.ParametersList[i].Type, interner);

            if(arguments[i] is UnboundLambdaExpression or MethodGroupExpression)
            {
                callables.Add(i);
                MatchStated(declared, arguments[i], lambdas, interner, method.TypeParametersList, bounds);
                continue;
            }

            MatchArgument(declared, arguments[i], method.TypeParametersList, bounds);
        }

        var blocked = InferFromCallables(method, arguments, callables, receiver, interner, lambdas, bounds);
        var fixedTypes = Fix(method.TypeParametersList, bounds, interner, out conflict);
        conflict = blocked ?? conflict;

        return fixedTypes;
    }

    /// <summary>
    /// type-argument-inference Requirement 11: a lambda or a method group is typed once the type
    /// parameters its parameter types mention can be fixed, and its result teaches the rest. The conflict
    /// returned is the one that kept a callable from being typed, which is the cause of whatever that
    /// callable would have taught.
    /// </summary>
    private static TypeArgumentConflict? InferFromCallables(
        IOverload method,
        IReadOnlyList<Expression> arguments,
        List<int> pending,
        TypeSubstitution receiver,
        TypeInterner interner,
        ILambdaTrials? lambdas,
        List<IMistType>[] bounds)
    {
        var parameters = method.TypeParametersList;
        var frozen = new IMistType?[bounds.Length];

        for(var progressed = true; progressed;)
        {
            progressed = false;

            foreach(var i in pending.ToList())
            {
                var declared = receiver.Apply(method.ParametersList[i].Type, interner);
                var shape = arguments[i] is UnboundLambdaExpression
                    ? ContextualLambdaTarget.Of(declared, interner)
                    : Shape(declared, interner);

                if(shape is null)
                {
                    _ = pending.Remove(i);
                    continue;
                }

                var stated = arguments[i] is UnboundLambdaExpression lambda && lambdas is not null
                    ? lambdas.StatedParameterTypes(lambda)
                    : [];
                var inputs = shape.ParameterTypes.Where((_, k) => k >= stated.Count || stated[k] is null).ToList();

                if(Freeze(inputs, parameters, bounds, frozen, interner, out var conflict) is not true)
                {
                    if(conflict is not null)
                    {
                        return conflict;
                    }

                    continue;
                }

                _ = pending.Remove(i);
                progressed = true;

                if(!parameters.Any(parameter => IsOpen(parameter, frozen) && Mentions(shape.ReturnType, parameter)))
                {
                    continue;
                }

                var substitution = TypeSubstitution.ForParameters(parameters, frozen);
                var given = shape.ParameterTypes
                    .Select((type, k) => k < stated.Count && stated[k] is { } own ? own : substitution.Apply(type, interner))
                    .ToList();

                var output = arguments[i] switch
                {
                    UnboundLambdaExpression unbound => lambdas?.ResultType(unbound, given),
                    MethodGroupExpression group => MethodGroupConversion.ResultType(group, given, interner),
                    _ => null,
                };

                if(output is not null)
                {
                    Learn(shape.ReturnType, output, parameters, bounds, frozen);
                }
            }
        }

        return null;
    }

    private static bool? Freeze(
        List<IMistType> inputs,
        IReadOnlyList<TypeParameter> parameters,
        List<IMistType>[] bounds,
        IMistType?[] frozen,
        TypeInterner interner,
        out TypeArgumentConflict? conflict)
    {
        conflict = null;
        var needed = parameters
            .Where(parameter => IsOpen(parameter, frozen) && inputs.Exists(input => Mentions(input, parameter)))
            .ToList();

        if(needed.Exists(parameter => bounds[parameter.Ordinal].Count == 0))
        {
            return null;
        }

        foreach(var parameter in needed)
        {
            if(Dominant(bounds[parameter.Ordinal], interner) is not { } fixedType)
            {
                conflict = new TypeArgumentConflict(parameter, bounds[parameter.Ordinal]);
                return false;
            }

            frozen[parameter.Ordinal] = fixedType;
        }

        return true;
    }

    private static bool IsOpen(TypeParameter parameter, IMistType?[] frozen)
    {
        return parameter.Ordinal < frozen.Length && frozen[parameter.Ordinal] is null;
    }

    private static void Learn(
        IMistType declared, IMistType output, IReadOnlyList<TypeParameter> parameters, List<IMistType>[] bounds, IMistType?[] frozen)
    {
        var learned = NewBounds(bounds.Length);
        Match(declared, output, parameters, learned);

        for(var j = 0; j < bounds.Length; j++)
        {
            if(frozen[j] is null)
            {
                learned[j].ForEach(bound => Add(bounds[j], bound));
            }
        }
    }

    private static void MatchStated(
        IMistType declared,
        Expression argument,
        ILambdaTrials? lambdas,
        TypeInterner interner,
        IReadOnlyList<TypeParameter> parameters,
        List<IMistType>[] bounds)
    {
        if(argument is not UnboundLambdaExpression lambda || lambdas is null || ContextualLambdaTarget.Of(declared, interner) is not { } shape)
        {
            return;
        }

        var stated = lambdas.StatedParameterTypes(lambda);

        for(var k = 0; k < stated.Count && k < shape.ParameterTypes.Count; k++)
        {
            if(stated[k] is { } type)
            {
                Match(shape.ParameterTypes[k], type, parameters, bounds);
            }
        }
    }

    private static IFunctionType? Shape(IMistType declared, TypeInterner interner)
    {
        return DelegateShape.Of(AliasTransparency.Expand(declared, interner));
    }

    private static bool Mentions(IMistType type, TypeParameter parameter)
    {
        return type switch
        {
            TypeParameter found => ReferenceEquals(found, parameter),
            ConstructedType constructed => constructed.TypeArguments.Any(argument => Mentions(argument, parameter))
                || (constructed.Enclosing is { } enclosing && Mentions(enclosing, parameter)),
            FunctionType function => Mentions(function.ReturnType, parameter)
                || function.ParameterTypes.Any(input => Mentions(input, parameter)),
            UnionType union => union.Constituents.Any(constituent => Mentions(constituent, parameter)),
            TupleType tuple => tuple.ElementTypes.Any(element => Mentions(element, parameter)),
            NamedTupleType named => named.ElementTypes.Any(element => Mentions(element, parameter)),
            RefinedType refined => Mentions(refined.BaseType, parameter),
            SpreadType spread => Mentions(spread.Operand, parameter),
            _ => false,
        };
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

        return Closed(bounds);
    }

    /// <summary>
    /// The type arguments that make a generic method's signature a FUNCTION TYPE, or null where a parameter
    /// stays open or meets two types. A method named as a value has no arguments to learn from, so the
    /// target's positions teach it, the result included where the target states one; each parameter takes
    /// exactly the type standing there, a delegate binding by identity where a call would widen. The
    /// receiver's own arguments are not applied first: they can mention none of the method's parameters,
    /// and the caller holds the closed signature to the target afterwards.
    /// </summary>
    public static IReadOnlyList<IMistType>? FromSignature(
        Method method, IReadOnlyList<IMistType> parameterTypes, IMistType? returnType)
    {
        if(method.ParametersList.Count != parameterTypes.Count)
        {
            return null;
        }

        var bounds = NewBounds(method.TypeParametersList.Count);

        for(var i = 0; i < method.ParametersList.Count; i++)
        {
            Match(method.ParametersList[i].Type, parameterTypes[i], method.TypeParametersList, bounds);
        }

        if(returnType is not null)
        {
            Match(method.ReturnType, returnType, method.TypeParametersList, bounds);
        }

        return Closed(bounds);
    }

    private static IMistType[]? Closed(List<IMistType>[] bounds)
    {
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
            MatchArgument(receiver.Apply(parameters[i].Type, interner), arguments[i],
                method.TypeParametersList, bounds);
        }

        for(var i = 0; i < trailing; i++)
        {
            var parameter = parameters[parameters.Count - 1 - i];
            var argument = arguments[arguments.Count - 1 - i];
            MatchArgument(receiver.Apply(parameter.Type, interner), argument,
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

    // An argument teaches a type parameter through the PRESENT constituent of an optional slot --
    // `Take<T>(T? value)` learns int from a 3 -- and a `void` teaches nothing, having taken its own
    // type FROM that slot: matching it would fix T to itself and emit it unbound at the call. Only
    // the optional is peeled; reading a general union's constituents is a wider question the caller
    // settles by stating the argument (type-argument-inference Requirement 3).
    private static void MatchArgument(
        IMistType declared, Expression argument, IReadOnlyList<TypeParameter> parameters, List<IMistType>[] bounds)
    {
        if(argument is VoidLiteralExpression)
        {
            return;
        }

        Match(declared, argument.Type, parameters, bounds);
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

        if(OptionalUnion.InnerType(declared) is { } present)
        {
            var given = OptionalUnion.InnerType(actual) ?? actual;

            if(!OptionalUnion.IsVoid(given))
            {
                Match(present, given, parameters, bounds);
            }

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

        Add(bounds[parameter.Ordinal], actual);
    }

    private static void Add(List<IMistType> slot, IMistType actual)
    {
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