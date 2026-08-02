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

        writer.WriteLine($"{modifiers}{EmitSignature(method)}");
        ConstraintEmitter.Emit(method.TypeParametersList, writer);
        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitBlock(method.Body, writer, diagnostics);
            }
        }
    }

    /// <summary>
    /// Everything of a method declaration but its modifiers and its body, shared with the
    /// interface member path in <see cref="TypeEmitter"/> so the two cannot disagree about
    /// what a method's signature contains.
    /// </summary>
    internal static string EmitSignature(Method method)
    {
        var returnType = TypeNameEmitter.Emit(method.ReturnType);
        var typeParams = method.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", method.TypeParametersList.Select(tp => tp.Name))}>"
            : "";
        var parameters = ParameterEmitter.Emit(method.ParametersList);

        return $"{returnType} {method.Name}{typeParams}({parameters})";
    }
}