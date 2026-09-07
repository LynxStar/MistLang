using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a parameter list as it appears between the parentheses of a declaration.
/// </summary>
internal static class ParameterEmitter
{
    public static string Emit(IReadOnlyList<Parameter> parameters, DiagnosticBag diagnostics)
    {
        if(parameters.Count == 0)
        {
            return "";
        }

        var parts = new List<string>(parameters.Count);
        for(var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];

            parts.Add(
                AttributeEmitter.Inline(parameter.DeclaredAttributesList, diagnostics)
                + $"{RefKeywords.For(parameter.Modifier.IsRef, parameter.Modifier.IsReadonly)}"
                + Collects(parameter, isLast: i == parameters.Count - 1)
                + $"{TypeNameEmitter.Emit(parameter.Type)} {parameter.Name}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The C# <c>params</c> modifier, for the homogeneous form alone. A value PACK is flattened too
    /// but collects into a tuple, which C# cannot declare that way -- the call site packs its
    /// arguments instead, so declaring it would make the same list flatten twice.
    /// </summary>
    private static string Collects(Parameter parameter, bool isLast)
    {
        return isLast && parameter.IsFlattened && parameter.Type is not (TupleType or NamedTupleType)
            ? "params "
            : "";
    }
}