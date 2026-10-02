using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The lowering of <c>where T : default</c>. C# has no such constraint, and Mist's default of a class is
/// an instance rather than a null, so a generic body cannot ask C# for it: each constrained parameter
/// gains a factory PARAMETER that the call site fills from the type argument it chose, and
/// <c>default(T)</c> in the body calls that.
/// <para>
/// A witness comes FIRST in the parameter list rather than last, because a declared <c>params</c> array
/// has to stay last and C# rejects a parameter after it.
/// </para>
/// <para>
/// The constraint therefore does NOT survive into metadata, so an instantiation from outside the
/// compilation is unchecked. Carrying it would mean a synthesized interface with a static abstract
/// factory, the way <see cref="ConstructorConstraintInterfaces"/> carries the parameterized
/// constructor constraint -- and that closes only over types DECLARED in Mist, where a default
/// constraint is legitimately closed by <c>int</c> and by an enum, neither of which can be given a
/// base list.
/// </para>
/// </summary>
internal static class DefaultConstraintWitness
{
    public static bool Constrains(IMistType type)
    {
        return type is TypeParameter parameter && Defaultability.IsDefaultConstrained(parameter);
    }

    public static string Name(TypeParameter parameter)
    {
        return $"__default_{DependentCarrierNames.Parameter(parameter)}";
    }

    /// <summary>
    /// The witness parameters a declaration carries, in the order its type parameters were declared.
    /// </summary>
    public static List<string> Parameters(IReadOnlyList<TypeParameter> typeParameters)
    {
        var witnesses = new List<string>();

        foreach(var parameter in Constrained(typeParameters))
        {
            witnesses.Add(Parameter(parameter));
        }

        return witnesses;
    }

    public static string Parameter(TypeParameter parameter)
    {
        return $"System.Func<{DependentCarrierNames.Parameter(parameter)}> {Name(parameter)}";
    }

    /// <summary>
    /// The witness arguments a call supplies, each a lambda over the default of the type argument that
    /// filled the parameter. Empty when the call fills none of them, which is every non-generic call.
    /// <para>
    /// A NULL ENTRY IS KEPT rather than dropped: a type argument with no default violates the
    /// constraint, and shortening the list instead would reach C# as an arity error naming a parameter
    /// nobody wrote.
    /// </para>
    /// </summary>
    public static List<string?> Arguments(
        IReadOnlyList<TypeParameter> typeParameters, IReadOnlyList<IMistType> typeArguments)
    {
        var witnesses = new List<string?>();

        for(var i = 0; i < typeParameters.Count && i < typeArguments.Count; i++)
        {
            if(!Constrains(typeParameters[i]))
            {
                continue;
            }

            var filling = typeParameters[i].IsPack && MonomorphReference.Elements(TypeNameEmitter.Closed(typeArguments[i])) is { } elements
                ? elements
                : [typeArguments[i]];

            witnesses.AddRange(filling.Select(
                argument => DefaultSynthesis.Expression(argument) is { } value ? $"() => {value}" : null));
        }

        return witnesses;
    }

    private static IEnumerable<TypeParameter> Constrained(IReadOnlyList<TypeParameter> typeParameters)
    {
        return typeParameters.Where(Constrains);
    }
}