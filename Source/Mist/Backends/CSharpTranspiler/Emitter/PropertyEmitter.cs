using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a property declaration: modifiers, type, name, accessors and any initializer.
/// </summary>
internal static class PropertyEmitter
{
    public static void Emit(
        Property property, bool emitReadonly, bool readonlyGetter, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(IntrinsicCloneEmitter.Forwards(property.Modifiers, property.DeclaringType))
        {
            IntrinsicCloneEmitter.EmitProperty(property, writer);
            return;
        }

        var modifiers = MemberModifierEmitter.Emit(property.Modifiers, emitReadonly);
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
            EmitAccessors(property, readonlyGetter, writer, diagnostics);
            writer.Dedent();

            // Written by hand rather than through CSharpWriter.Block, whose closing brace ends its
            // line, because an initializer has to follow that brace on the same line.
            writer.Write("}");
        }

        StatementHost.Hosted? hosted = null;

        if(property.Initializer is { } initializer)
        {
            writer.Append(" = ");
            hosted = StatementHost.Emit(initializer, $"__Initialize{property.Name}", property.Type, [], writer, diagnostics);
            writer.Append(";");
        }

        writer.AppendLine();
        StatementHost.EmitMethods(hosted is null ? [] : [hosted], writer, diagnostics);
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

    private static void EmitAccessors(
        Property property, bool readonlyGetter, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(property.Getter is { } getter)
        {
            AccessorEmitter.Emit(getter, isSetter: false, readonlyGetter, writer, diagnostics);
        }

        if(property.Setter is { } setter)
        {
            AccessorEmitter.Emit(setter, isSetter: true, isReadonly: false, writer, diagnostics);
        }
    }
}