using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a field declaration: modifiers, type, name, and initializer.
/// </summary>
internal static class FieldEmitter
{
    public static void Emit(Field field, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        Emit(field, field.Type, field.Modifiers, writer, diagnostics);
    }

    /// <summary>
    /// Emits a field flattened from a struct's ancestor, whose emitted type and accessibility can
    /// both differ from the declaration -- see <see cref="Lowering.FlattenedField"/>.
    /// </summary>
    public static void Emit(Lowering.FlattenedField field, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = field.Field.Modifiers with { Accessibility = field.Accessibility };
        Emit(field.Field, field.Type, modifiers, writer, diagnostics);
    }

    private static void Emit(
        Field field,
        IMistType type,
        MemberModifiers modifiers,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var emittedModifiers = MemberModifierEmitter.Emit(modifiers);
        var typeName = TypeNameEmitter.Emit(type);

        if(field.Initializer is null)
        {
            writer.WriteLine($"{emittedModifiers}{typeName} {field.Name};");
            return;
        }

        writer.Write($"{emittedModifiers}{typeName} {field.Name} = ");
        BodyEmitter.EmitExpression(field.Initializer, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
    }
}