using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a field declaration: modifiers, type, name, and initializer.
/// </summary>
internal static class FieldEmitter
{
    public static void Emit(Field field, bool inReadonlyStruct, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        Emit(field, field.Type, field.Modifiers, inReadonlyStruct, writer, diagnostics);
    }

    /// <summary>
    /// Emits a field flattened from a struct's ancestor, whose emitted type and accessibility can
    /// both differ from the declaration -- see <see cref="Lowering.FlattenedField"/>.
    /// </summary>
    public static void Emit(
        Lowering.FlattenedField field, bool inReadonlyStruct, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = field.Field.Modifiers with { Accessibility = field.Accessibility };
        Emit(field.Field, field.Type, modifiers, inReadonlyStruct, writer, diagnostics);
    }

    private static void Emit(
        Field field,
        IMistType type,
        MemberModifiers modifiers,
        bool inReadonlyStruct,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        // C# refuses a writable instance field on a readonly struct (CS8340), so the type's modifier
        // has to reach every field it declares rather than standing alone on the declaration.
        var emittedModifiers = MemberModifierEmitter.Emit(
            modifiers, emitReadonly: modifiers.IsReadonly || inReadonlyStruct);
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