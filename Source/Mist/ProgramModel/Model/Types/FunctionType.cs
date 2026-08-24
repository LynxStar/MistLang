using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A structural function type, interned by its parameter types and result.
/// <para>
/// NOT a construction of <c>Standard.Func</c>, though that is what it emits as. Func is declared
/// <c>Func&lt;params TArgs, TResult&gt;</c> -- one variadic type rather than an overload family -- and
/// resolving that declaration needs type packs, which arrive far later in the progression than function
/// types do. Keeping the type structural is what lets this layer stand on its own; the CLR family is
/// then an EMISSION concern, chosen by arity and by whether the result is void, exactly as a tuple's is.
/// </para>
/// </summary>
internal sealed class FunctionType : MistType, IFunctionType
{
    public FunctionType(IReadOnlyList<IMistType> parameterTypes, IMistType returnType)
        : base(BuildName(parameterTypes, returnType), new Scope(null), declaringType: null)
    {
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
    }

    public IReadOnlyList<IMistType> ParameterTypes { get; }

    public IMistType ReturnType { get; }

    /// <summary>
    /// The parameter types followed by the result, which is the order source writes and the order the
    /// interner keys on.
    /// </summary>
    public IReadOnlyList<IMistType> TypeArgumentsInSourceOrder()
    {
        var all = new IMistType[ParameterTypes.Count + 1];

        for(var i = 0; i < ParameterTypes.Count; i++)
        {
            all[i] = ParameterTypes[i];
        }

        all[^1] = ReturnType;
        return all;
    }

    private static string BuildName(IReadOnlyList<IMistType> parameterTypes, IMistType returnType)
    {
        var parts = new string[parameterTypes.Count + 1];

        for(var i = 0; i < parameterTypes.Count; i++)
        {
            parts[i] = parameterTypes[i].Name;
        }

        parts[^1] = returnType.Name;
        return $"func<{string.Join(", ", parts)}>";
    }
}