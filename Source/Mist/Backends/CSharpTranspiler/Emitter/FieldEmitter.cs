using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a field declaration: modifiers, type, name, and initializer.
/// </summary>
internal static class FieldEmitter
{
    public static void Emit(Field field, CSharpWriter writer)
    {
        var modifiers = MemberModifierEmitter.Emit(field.Modifiers);
        var typeName = TypeNameEmitter.Emit(field.Type);

        if(field.Initializer is null)
        {
            writer.WriteLine($"{modifiers}{typeName} {field.Name};");
            return;
        }

        writer.Write($"{modifiers}{typeName} {field.Name} = ");
        BodyEmitter.EmitExpression(field.Initializer, writer);
        writer.Append(";");
        writer.AppendLine();
    }
}