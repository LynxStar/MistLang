using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a property declaration: modifiers, type, name, accessors and any initializer, after the
/// backing field it declares (<see cref="BackingStorage"/>), which then carries the initializer.
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

        using var constructionScope = property.Modifiers.IsStatic ? ConstructorWitnesses.NativeScope() : null;
        var modifiers = MemberModifierEmitter.Emit(property.Modifiers, emitReadonly);
        var typeName = TypeNameEmitter.Emit(property.Type);
        var declaration = $"{modifiers}{typeName} {IdentifierSpelling.Of(property.Name)}";
        var storage = BackingStorage.DeclaredField(property);
        var hosted = storage is null ? null : EmitDeclaredField(property, storage, typeName, writer, diagnostics);

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
            EmitAccessors(property, readonlyGetter, storage, writer, diagnostics);
            writer.Dedent();

            // Written by hand rather than through CSharpWriter.Block, whose closing brace ends its
            // line, because an initializer has to follow that brace on the same line.
            writer.Write("}");
        }

        if(storage is null && property.Initializer is { } initializer && (property.Modifiers.IsStatic || !DefaultWitnesses.Deferred(initializer)))
        {
            writer.Append(" = ");
            hosted = StatementHost.Emit(initializer, $"__Initialize{property.Name}", property.Type, [], writer, diagnostics);
            writer.Append(";");
        }

        writer.AppendLine();
        StatementHost.EmitMethods(hosted is null ? [] : [hosted], writer, diagnostics);
    }

    private static StatementHost.Hosted? EmitDeclaredField(
        Property property, string name, string typeName, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var isStatic = property.Modifiers.IsStatic;
        var modifiers = isStatic ? "private static " : property.DeclaringType is { IsReadonly: true } ? "private readonly " : "private ";

        if(property.Initializer is null || (!isStatic && DefaultWitnesses.Deferred(property.Initializer)))
        {
            writer.WriteLine($"{modifiers}{typeName} {name};");
            return null;
        }

        writer.Write($"{modifiers}{typeName} {name} = ");
        var hosted = StatementHost.Emit(property.Initializer, $"__Initialize{property.Name}", property.Type, [], writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
        return hosted;
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
        Property property, bool readonlyGetter, string? storage, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(property.Getter is { } getter)
        {
            if(storage is not null && AccessorEmitter.IsBodiless(getter))
            {
                AccessorEmitter.EmitOverStorage(getter, isSetter: false, readonlyGetter, storage, writer);
            }
            else
            {
                AccessorEmitter.Emit(getter, isSetter: false, readonlyGetter, writer, diagnostics, property.Type);
            }
        }

        if(property.Setter is { } setter)
        {
            if(storage is not null && AccessorEmitter.IsBodiless(setter))
            {
                AccessorEmitter.EmitOverStorage(setter, isSetter: true, isReadonly: false, storage, writer);
            }
            else
            {
                AccessorEmitter.Emit(setter, isSetter: true, isReadonly: false, writer, diagnostics);
            }
        }
    }
}