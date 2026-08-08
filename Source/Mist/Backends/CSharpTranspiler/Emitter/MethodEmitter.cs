using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a method declaration: modifiers, return type, name, parameters, and body.
/// </summary>
internal static class MethodEmitter
{
    public static void Emit(
        Method method,
        Storm.Runtime.SyntaxNode? locationNode,
        bool inStruct,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        ParameterEmitter.ReportUnsupportedRef(method.ParametersList, locationNode, diagnostics);

        // A readonly method is a struct-only concept in C#, so on a class the modifier is dropped
        // rather than passed through -- Mist admits it there and CS0106 refuses it.
        var modifiers = MemberModifierEmitter.Emit(
            method.Modifiers, emitReadonly: inStruct && method.Modifiers.IsReadonly);

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