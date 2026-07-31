using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits an indexer declaration: <c>type this[params] { get; set; }</c>.
/// </summary>
internal static class IndexerEmitter
{
    public static void Emit(Indexer indexer, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = MemberModifierEmitter.Emit(indexer.Modifiers);
        var type = TypeNameEmitter.Emit(indexer.Type);
        var parameters = ParameterEmitter.Emit(indexer.ParametersList);

        writer.WriteLine($"{modifiers}{type} this[{parameters}]");
        using(writer.Block())
        {
            if(indexer.Getter is not null)
            {
                EmitAccessor("get", indexer.Getter, writer, diagnostics);
            }

            if(indexer.Setter is not null)
            {
                EmitAccessor("set", indexer.Setter, writer, diagnostics);
            }
        }
    }

    private static void EmitAccessor(string keyword, PropertyAccessor accessor, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(accessor.Body is not null)
        {
            writer.WriteLine(keyword);
            using(writer.Block())
            {
                BodyEmitter.EmitBlock(accessor.Body, writer, diagnostics);
            }
        }
        else if(accessor.ExpressionBody is not null)
        {
            writer.Write($"{keyword} => ");
            BodyEmitter.EmitExpression(accessor.ExpressionBody, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
        }
        else
        {
            writer.WriteLine($"{keyword};");
        }
    }
}