using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a property declaration: modifiers, type, name, accessors and any initializer.
/// </summary>
internal static class PropertyEmitter
{
    public static void Emit(Property property, bool inStruct, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = MemberModifierEmitter.Emit(
            property.Modifiers, emitReadonly: inStruct && property.Modifiers.IsReadonly);
        var declaration = $"{modifiers}{TypeNameEmitter.Emit(property.Type)} {property.Name}";

        AttributeEmitter.EmitLines(property.DeclaredAttributesList, writer, diagnostics);

        if(AccessorEmitter.IsBodiless(property.Getter) && AccessorEmitter.IsBodiless(property.Setter))
        {
            writer.Write($"{declaration} {{ {InlineAccessors(property)} }}");
        }
        else
        {
            writer.WriteLine(declaration);
            writer.WriteLine("{");
            writer.Indent();
            EmitAccessors(property, writer, diagnostics);
            writer.Dedent();

            // Written by hand rather than through CSharpWriter.Block, whose closing brace ends its
            // line, because an initializer has to follow that brace on the same line.
            writer.Write("}");
        }

        if(property.Initializer is { } initializer)
        {
            writer.Append(" = ");
            BodyEmitter.EmitExpression(initializer, writer, diagnostics);
            writer.Append(";");
        }

        writer.AppendLine();
    }

    private static string InlineAccessors(Property property)
    {
        var parts = new List<string>(2);

        if(property.Getter is { } getter)
        {
            parts.Add(AccessorEmitter.EmitBodiless(getter, isSetter: false));
        }

        if(property.Setter is { } setter)
        {
            parts.Add(AccessorEmitter.EmitBodiless(setter, isSetter: true));
        }

        return string.Join(" ", parts);
    }

    private static void EmitAccessors(Property property, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(property.Getter is { } getter)
        {
            AccessorEmitter.Emit(getter, isSetter: false, writer, diagnostics);
        }

        if(property.Setter is { } setter)
        {
            AccessorEmitter.Emit(setter, isSetter: true, writer, diagnostics);
        }
    }
}