using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class ResultDropEmitter
{
    public static bool Needed(IMistType type)
    {
        return ResultTransfers.Payloads(TypeNameEmitter.Closed(type)).Any(payload => LocalLifetimes.IsTracked(payload) || LocalLifetimes.IsScoped(payload));
    }

    public static string? Release(string receiver, IMistType type)
    {
        type = TypeNameEmitter.Closed(type);
        if(!Needed(type))
        {
            return null;
        }

        return ResultTypes.Of(type) is not null ? $"({receiver}).Inspect().Drop()"
            : ResultTypes.Projection(type) is { } projection ? DropInsertion.DropChain($"({receiver}).Payload", projection.Payload)
            : ResultTransfers.Inspection(type) is not null ? $"({receiver}).Drop()" : null;
    }

    public static void Emit(UnionType union, CSharpWriter writer)
    {
        if(ResultTransfers.Inspection(union) is null || !Needed(union))
        {
            return;
        }

        writer.WriteBlankLine();
        writer.WriteLine("public void Drop()");
        using(writer.Block())
        {
            for(var i = 0; i < union.Constituents.Count; i++)
            {
                var tuple = (ITupleType)union.Constituents[i];
                var payload = tuple.ElementTypes[1];
                if(!LocalLifetimes.IsTracked(payload) && !LocalLifetimes.IsScoped(payload))
                {
                    continue;
                }

                writer.WriteLine($"if (_tag == {i + 1})");
                using(writer.Block())
                {
                    var read = $"(({TypeNameEmitter.Emit(tuple)})_value).Item2";
                    writer.WriteLine($"{DropInsertion.DropChain(read, payload)};");
                }
            }
        }
    }
}