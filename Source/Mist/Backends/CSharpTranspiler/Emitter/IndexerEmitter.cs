using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits an indexer declaration: <c>type this[params] { get; set; }</c>.
/// </summary>
internal static class IndexerEmitter
{
    public static void Emit(
        Indexer indexer, bool emitReadonly, bool readonlyGetter, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = MemberModifierEmitter.Emit(indexer.Modifiers, emitReadonly);
        var type = TypeNameEmitter.Emit(indexer.Type);
        var parameters = ParameterEmitter.Emit(indexer.ParametersList, diagnostics);

        AttributeEmitter.EmitLines(indexer.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{modifiers}{type} this[{parameters}]");
        using(writer.Block())
        {
            if(indexer.Getter is { } getter)
            {
                AccessorEmitter.Emit(getter, isSetter: false, readonlyGetter, writer, diagnostics);
            }

            if(indexer.Setter is { } setter)
            {
                AccessorEmitter.Emit(setter, isSetter: true, isReadonly: false, writer, diagnostics);
            }
        }
    }
}