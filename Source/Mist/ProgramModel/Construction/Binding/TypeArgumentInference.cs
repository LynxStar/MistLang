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
        Method method,
        IReadOnlyList<Expression> arguments,
        TypeSubstitution receiver,
        TypeInterner interner)
    {
        var inferred = new IMistType?[method.TypeParametersList.Count];

        if(inferred.Length == 0)
        {
            return inferred;
        }

        if(ValuePack.FlattenedSlot(method.ParametersList) is { } flattened)
        {
            InferAcrossPack(method, arguments, receiver, interner, inferred, flattened);
            return inferred;
        }

        // OverloadResolution.Select has already matched the counts exactly, so a mismatch here is
        // not this method's to reject.
        var slots = Math.Min(method.ParametersList.Count, arguments.Count);

        for(var i = 0; i < slots; i++)
        {
            Match(receiver.Apply(method.ParametersList[i].Type, interner), arguments[i].Type,
                method.TypeParametersList, inferred);
        }

        return inferred;
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
        var inferred = new IMistType?[parameters.Count];
        Match(declared, actual, parameters, inferred);

        var closed = new IMistType[inferred.Length];

        for(var i = 0; i < inferred.Length; i++)
        {
            if(inferred[i] is not { } slot)
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
        Method method,
        IReadOnlyList<Expression> arguments,
        TypeSubstitution receiver,
        TypeInterner interner,
        IMistType?[] inferred,
        int flattened)
    {
        var parameters = method.ParametersList;
        var trailing = parameters.Count - flattened - 1;

        for(var i = 0; i < flattened && i < arguments.Count; i++)
        {
            Match(receiver.Apply(parameters[i].Type, interner), arguments[i].Type,
                method.TypeParametersList, inferred);
        }

        for(var i = 0; i < trailing; i++)
        {
            var parameter = parameters[parameters.Count - 1 - i];
            var argument = arguments[arguments.Count - 1 - i];
            Match(receiver.Apply(parameter.Type, interner), argument.Type,
                method.TypeParametersList, inferred);
        }

        if(ValuePack.PackParameterOf(parameters[flattened]) is not { } pack)
        {
            return;
        }

        var absorbed = ValuePack.Absorbed(
            parameters, [.. arguments.Select(a => a.Type)], flattened);

        var domain = pack.Domain ?? pack;
        Record(pack, interner.LiftPack(domain, new TypePackValue(absorbed)),
            method.TypeParametersList, inferred);
    }

    /// <summary>
    /// The two constructions must share a definition, so an argument that reaches the declared type
    /// only through one of its own bases infers nothing.
    /// </summary>
    private static void Match(
        IMistType declared, IMistType actual, IReadOnlyList<TypeParameter> parameters, IMistType?[] inferred)
    {
        if(declared is TypeParameter parameter)
        {
            Record(parameter, actual, parameters, inferred);
            return;
        }

        // A function argument carries bindings in both directions: its RESULT names a parameter that
        // nothing else in the signature mentions -- `Apply(Func<TArgs..., TResult> f, params TArgs)`
        // has no other way to learn TResult -- and its own parameters match position for position
        // once a spread among them is skipped, that part being the pack binding's to make.
        if(declared is FunctionType declaredFunction && actual is FunctionType actualFunction)
        {
            Match(declaredFunction.ReturnType, actualFunction.ReturnType, parameters, inferred);

            if(declaredFunction.ParameterTypes.Count == actualFunction.ParameterTypes.Count)
            {
                for(var i = 0; i < declaredFunction.ParameterTypes.Count; i++)
                {
                    Match(declaredFunction.ParameterTypes[i], actualFunction.ParameterTypes[i], parameters, inferred);
                }
            }

            return;
        }

        if(declared is not ConstructedType declaredConstructed || actual is not ConstructedType actualConstructed)
        {
            return;
        }

        var sameDefinition = string.Equals(
            declaredConstructed.Definition.FullyQualifiedName,
            actualConstructed.Definition.FullyQualifiedName,
            StringComparison.Ordinal);

        if(!sameDefinition || declaredConstructed.TypeArguments.Count != actualConstructed.TypeArguments.Count)
        {
            return;
        }

        for(var i = 0; i < declaredConstructed.TypeArguments.Count; i++)
        {
            Match(declaredConstructed.TypeArguments[i], actualConstructed.TypeArguments[i], parameters, inferred);
        }
    }

    /// <summary>
    /// A parameter the method does not declare is the receiver's to close, not this call's, so it is
    /// passed over rather than refused -- including where the receiver was not constructed and so left
    /// it open.
    /// </summary>
    private static void Record(
        TypeParameter parameter, IMistType actual, IReadOnlyList<TypeParameter> parameters, IMistType?[] inferred)
    {
        if(!parameters.Contains(parameter) || parameter.Ordinal >= inferred.Length)
        {
            return;
        }

        inferred[parameter.Ordinal] = inferred[parameter.Ordinal] is { } existing
            ? Wider(existing, actual)
            : actual
            ;
    }

    /// <summary>
    /// Which of two answers for the same parameter to keep, when it appears in more than one slot: the
    /// one the other converts into, so <c>F(1, 2L)</c> against <c>F&lt;T&gt;(T, T)</c> answers
    /// <c>long</c> rather than the <c>int</c> that came first and that the second argument would not
    /// fit.
    /// <para>
    /// Two answers neither of which converts keep the earlier one, where C# rejects the call for having
    /// no best common type. A lone candidate is then bound on an arbitrary one of the two, since
    /// selection does not weigh conversions when there is nothing to select between.
    /// </para>
    /// </summary>
    private static IMistType Wider(IMistType existing, IMistType candidate)
    {
        if(ConversionRelation.IsWithin(candidate, existing))
        {
            return existing;
        }

        return ConversionRelation.IsWithin(existing, candidate) ? candidate : existing;
    }
}