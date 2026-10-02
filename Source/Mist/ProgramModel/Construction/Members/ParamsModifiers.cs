using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// What one value-parameter list's <c>params</c> modifiers are refused for, decided on the resolved
/// parameters so that every kind of list -- a method's, a constructor's, an abstract slot's, a local
/// function's -- is judged by the one reading.
/// </summary>
internal static class ParamsModifiers
{
    public static IReadOnlyList<int> Surplus(IReadOnlyList<Parameter> parameters)
    {
        var surplus = new List<int>();
        var seen = false;

        for(var i = 0; i < parameters.Count; i++)
        {
            if(!parameters[i].IsFlattened)
            {
                continue;
            }

            if(seen)
            {
                surplus.Add(i);
            }

            seen = true;
        }

        return surplus;
    }

    public static string? WhyMalformed(Parameter parameter)
    {
        if(!parameter.IsFlattened)
        {
            return null;
        }

        if(!Collects(parameter.Type))
        {
            return $"its type '{TypeDisplay.Mist(parameter.Type)}' cannot collect a flat argument list";
        }

        return parameter.Modifier.IsRef ? "it is passed by reference, where the arguments it collects are values" : null;
    }

    private static bool Collects(IMistType type)
    {
        return type is TupleType or NamedTupleType or ErrorType || ParamsCollection.ElementOf(type) is not null;
    }
}