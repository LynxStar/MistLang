using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class IndexerEmitter
{
    public static void Emit(
        Indexer indexer, bool emitReadonly, bool readonlyGetter, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(OverloadNames.Renamed(indexer) is { } renamed)
        {
            EmitAccessors(indexer, renamed, emitReadonly, readonlyGetter, writer, diagnostics);
            return;
        }

        var metadataName = IndexerMetadataName.Of(indexer.DeclaringType);
        if(metadataName != "Item")
        {
            writer.WriteLine($"[System.Runtime.CompilerServices.IndexerName(\"{metadataName}\")]");
        }

        if(IntrinsicCloneEmitter.Forwards(indexer.Modifiers, indexer.DeclaringType))
        {
            IntrinsicCloneEmitter.EmitIndexer(indexer, writer, diagnostics);
            return;
        }

        var modifiers = MemberModifierEmitter.Emit(indexer.Modifiers, emitReadonly, NativeSlotFills.Fills(indexer));
        var type = TypeNameEmitter.Emit(indexer.Type);
        var parameters = ParameterEmitter.Emit(indexer.ParametersList, diagnostics);

        AttributeEmitter.EmitLines(indexer.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{modifiers}{type} this[{parameters}]");
        using(writer.Block())
        {
            if(indexer.Getter is { } getter)
            {
                AccessorEmitter.Emit(getter, isSetter: false, readonlyGetter, writer, diagnostics, indexer.Type);
            }

            if(indexer.Setter is { } setter)
            {
                AccessorEmitter.Emit(setter, isSetter: true, isReadonly: false, writer, diagnostics);
            }
        }
    }

    private static void EmitAccessors(
        Indexer indexer, string renamed, bool emitReadonly, bool readonlyGetter, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var type = TypeNameEmitter.Emit(indexer.Type);
        var parameters = ParameterEmitter.Emit(indexer.ParametersList, diagnostics);

        AttributeEmitter.EmitLines(indexer.DeclaredAttributesList, writer, diagnostics);

        if(indexer.Getter is { } getter)
        {
            var head = $"{Modifiers(indexer, getter, emitReadonly || readonlyGetter)}{type} {OverloadNames.Getter(renamed)}({parameters})";

            if(getter.Body is { } body)
            {
                writer.WriteLine(head);
                using(writer.Block())
                {
                    BodyEmitter.EmitBlock(body, writer, diagnostics);
                }
            }
            else if(getter.ExpressionBody is { } expression)
            {
                writer.WriteLine(head);
                using(writer.Block())
                {
                    BodyEmitter.EmitSequenced(expression, "return ", writer, diagnostics, slot: indexer.Type);
                }
            }
            else
            {
                writer.WriteLine($"{head};");
            }
        }

        if(indexer.Setter is { } setter)
        {
            var head = $"{Modifiers(indexer, setter, emitReadonly)}{type} {OverloadNames.Setter(renamed)}({parameters}, {type} value)";

            if(setter.Body is { } body)
            {
                writer.WriteLine(head);
                using(writer.Block())
                {
                    BodyEmitter.EmitSetterBody(body, writer, diagnostics);
                }
            }
            else if(setter.ExpressionBody is { } expression)
            {
                writer.WriteLine(head);
                using(writer.Block())
                {
                    BodyEmitter.EmitSequenced(expression, null, writer, diagnostics);
                    writer.WriteLine("return value;");
                }
            }
            else
            {
                writer.WriteLine($"{head};");
            }
        }
    }

    private static string Modifiers(Indexer indexer, PropertyAccessor accessor, bool isReadonly)
    {
        var modifiers = accessor.Accessibility == Accessibility.Private ? indexer.Modifiers with { Accessibility = Accessibility.Private } : indexer.Modifiers;

        return MemberModifierEmitter.Emit(modifiers, isReadonly);
    }
}