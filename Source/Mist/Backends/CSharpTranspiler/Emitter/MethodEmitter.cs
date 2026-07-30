using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a method declaration: modifiers, return type, name, parameters, and body.
/// </summary>
internal static class MethodEmitter
{
    public static void Emit(Method method, Storm.Runtime.SyntaxNode? locationNode, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        ParameterEmitter.ReportUnsupportedRef(method.ParametersList, locationNode, diagnostics);

        var modifiers = MemberModifierEmitter.Emit(method.Modifiers);
        var returnType = TypeNameEmitter.Emit(method.ReturnType);
        var typeParams = method.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", method.TypeParametersList.Select(tp => tp.Name))}>"
            : "";
        var parameters = ParameterEmitter.Emit(method.ParametersList);

        writer.WriteLine($"{modifiers}{returnType} {method.Name}{typeParams}({parameters})");
        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitBlock(method.Body, writer, diagnostics);
            }
        }
    }
}