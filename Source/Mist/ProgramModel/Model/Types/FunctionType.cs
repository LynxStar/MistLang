using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Never a construction of <c>Standard.Func</c>: those collapse into this type at
/// <see cref="Construction.Resolution.ScopeResolver.AsFunctionType"/>, and the CLR Func/Action choice
/// is made at emission.
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