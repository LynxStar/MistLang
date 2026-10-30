using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a field declaration: modifiers, type, name, and initializer.
/// </summary>
internal static class FieldEmitter
{
    public static void Emit(Field field, bool inReadonlyType, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        Emit(field, field.Type, field.Modifiers, inReadonlyType, writer, diagnostics);
    }

    /// <summary>
    /// Emits a field flattened from a struct's ancestor, whose emitted type and accessibility can
    /// both differ from the declaration -- see <see cref="FlattenedField"/>.
    /// </summary>
    public static void Emit(
        FlattenedField field, bool inReadonlyType, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = field.Field.Modifiers with { Accessibility = field.Accessibility };
        Emit(field.Field, field.Type, modifiers, inReadonlyType, writer, diagnostics);
    }

    private static void Emit(
        Field field,
        IMistType type,
        MemberModifiers modifiers,
        bool inReadonlyType,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(modifiers.IsRef)
        {
            throw new InvalidOperationException(
                $"ICE: ref field '{field.Name}' reached emission; {nameof(DeclarationModifierDiagnostics.RefOnField)} refuses it at its declaration.");
        }

        // A readonly type's modifier reaches every field it declares rather than standing alone on the
        // declaration, and for two unrelated reasons. C# REFUSES a writable instance field on a
        // readonly struct (CS8340). And it has no `readonly class` at all, so on a class the field is
        // the only place the type's readonly-ness survives emission.
        using var constructionScope = modifiers.IsStatic ? ConstructorWitnesses.NativeScope() : null;
        AttributeEmitter.EmitLines(field.DeclaredAttributesList, writer, diagnostics);

        var emittedModifiers = MemberModifierEmitter.Emit(
            modifiers, emitReadonly: modifiers.IsReadonly || inReadonlyType);
        var typeName = TypeNameEmitter.Emit(type);

        if(field.Initializer is null || (!modifiers.IsStatic && DefaultWitnesses.Deferred(field.Initializer)))
        {
            // AllowNull admits CLR-zero storage without an initializer that can reset earlier static writes.
            if(field.Initializer is null && field.DeclaringType is ClassType
                && !TypeNameEmitter.IsValueKind(type)
                && Defaultability.IsUndecided(TypeNameEmitter.Closed(type)))
            {
                writer.WriteLine("[System.Diagnostics.CodeAnalysis.AllowNull]");
            }

            writer.WriteLine($"{emittedModifiers}{typeName} {IdentifierSpelling.Of(field.Name)};");
            return;
        }

        writer.Write($"{emittedModifiers}{typeName} {IdentifierSpelling.Of(field.Name)} = ");
        var hosted = StatementHost.Emit(field.Initializer, $"__Initialize{field.Name}", type, [], writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
        StatementHost.EmitMethods(hosted is null ? [] : [hosted], writer, diagnostics);
    }
}