using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a parameter list as it appears between the parentheses of a declaration.
/// </summary>
internal static class ParameterEmitter
{
    public static string Emit(IReadOnlyList<Parameter> parameters)
    {
        if(parameters.Count == 0)
        {
            return "";
        }

        var parts = new List<string>(parameters.Count);
        foreach(var parameter in parameters)
        {
            parts.Add($"{TypeNameEmitter.Emit(parameter.Type)} {parameter.Name}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Reports a diagnostic for each <c>ref</c> parameter in the list. Called before
    /// emission so the error points at the Mist source rather than surfacing as a
    /// silent miscompile in the generated C#.
    /// </summary>
    public static void ReportUnsupportedRef(
        IReadOnlyList<Parameter> parameters,
        Storm.Runtime.SyntaxNode? locationNode,
        DiagnosticBag diagnostics)
    {
        if(locationNode is null)
        {
            return;
        }

        foreach(var parameter in parameters)
        {
            if(parameter.Modifier.IsRef)
            {
                diagnostics.ReportAtNode(
                    BackendDiagnostics.UnsupportedRefParameter, locationNode, null, parameter.Name);
            }
        }
    }
}