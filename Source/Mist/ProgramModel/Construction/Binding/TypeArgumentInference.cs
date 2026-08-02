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

        // OverloadResolution.Select has already matched the counts exactly, so a mismatch here is
        // not this method's to reject.
        var slots = Math.Min(method.ParametersList.Count, arguments.Count);

        for(var i = 0; i < slots; i++)
        {
            Match(receiver.Apply(method.ParametersList[i].Type, interner), arguments[i].Type, method, inferred);
        }

        return inferred;
    }

    /// <summary>
    /// The two constructions must share a definition, so an argument that reaches the declared type
    /// only through one of its own bases infers nothing.
    /// </summary>
    private static void Match(IMistType declared, IMistType actual, Method method, IMistType?[] inferred)
    {
        if(declared is TypeParameter parameter)
        {
            Record(parameter, actual, method, inferred);
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
            Match(declaredConstructed.TypeArguments[i], actualConstructed.TypeArguments[i], method, inferred);
        }
    }

    /// <summary>
    /// A parameter the method does not declare is the receiver's to close, not this call's, so it is
    /// passed over rather than refused -- including where the receiver was not constructed and so left
    /// it open.
    /// </summary>
    private static void Record(TypeParameter parameter, IMistType actual, Method method, IMistType?[] inferred)
    {
        if(!method.TypeParametersList.Contains(parameter) || parameter.Ordinal >= inferred.Length)
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
        if(ConversionRelation.IsImplicit(candidate, existing))
        {
            return existing;
        }

        return ConversionRelation.IsImplicit(existing, candidate) ? candidate : existing;
    }
}