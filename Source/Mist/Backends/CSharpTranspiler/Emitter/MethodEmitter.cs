using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a method declaration: modifiers, return type, name, parameters, and body.
/// </summary>
internal static class MethodEmitter
{
    public static void Emit(
        Method method,
        bool inStruct,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        // A readonly method is a struct-only concept in C#, so on a class the modifier is dropped
        // rather than passed through -- Mist admits it there and CS0106 refuses it.
        var modifiers = MemberModifierEmitter.Emit(
            method.Modifiers, emitReadonly: inStruct && method.Modifiers.IsReadonly);

        AttributeEmitter.EmitLines(method.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{modifiers}{EmitSignature(method, diagnostics)}");
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
    /// Refuses the one <c>ref</c> spelling <see cref="EmitSignature"/> cannot resolve. Kept apart from
    /// the signature because that is also spelled for an interface member and for a restated default,
    /// so the report belongs at the sites that OWN a declaration -- restating someone else's must not
    /// report it twice.
    /// </summary>
    internal static void ReportReadonlyRefReturn(
        Method method, Storm.Runtime.SyntaxNode? locationNode, DiagnosticBag diagnostics)
    {
        if(method.Modifiers.IsRef && method.Modifiers.IsReadonly && locationNode is not null)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.ReadonlyRefReturn, locationNode, null, method.Name);
        }
    }

    /// <summary>
    /// Everything of a method declaration but its modifiers and its body, shared with the
    /// interface member path in <see cref="TypeEmitter"/> so the two cannot disagree about
    /// what a method's signature contains.
    /// </summary>
    internal static string EmitSignature(Method method, DiagnosticBag diagnostics)
    {
        // The one deliberate exception to Void emitting as Standard.Void: a return type takes
        // the native C# form, and the call-site lowering in BodyEmitter reconstructs the unit
        // value wherever a caller consumes it -- exact, because the type has one inhabitant.
        // Only the bare ref, never `ref readonly`: on a method `readonly` is ALREADY the struct
        // modifier that MemberModifierEmitter writes, so the pair has two readings and
        // ReadonlyRefReturn refuses it rather than either being picked here.
        var returnType = OptionalUnion.IsVoid(method.ReturnType)
            ? "void"
            : $"{RefKeywords.For(method.Modifiers.IsRef, isReadonly: false)}{TypeNameEmitter.Emit(method.ReturnType)}";
        var typeParams = method.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", method.TypeParametersList.Select(tp => tp.Name))}>"
            : "";
        var parameters = ParameterEmitter.Emit(method.ParametersList, diagnostics);

        return $"{returnType} {method.Name}{typeParams}({parameters})";
    }
}