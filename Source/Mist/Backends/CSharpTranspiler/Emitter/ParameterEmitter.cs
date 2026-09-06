using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

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
        foreach(var parameter in parameters)
        {
            parts.Add(
                AttributeEmitter.Inline(parameter.DeclaredAttributesList, diagnostics)
                + $"{RefKeywords.For(parameter.Modifier.IsRef, parameter.Modifier.IsReadonly)}"
                + $"{TypeNameEmitter.Emit(parameter.Type)} {parameter.Name}");
        }

        return string.Join(", ", parts);
    }

}