using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    private static void EmitTuplePattern(
        TuplePattern tuple, List<HoistedTest>? hoisted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var (widthMissed, tested) = PackTuplePatterns.Lowering(tuple);

        if(widthMissed)
        {
            writer.Append("(object and ");
        }

        if(tested is not null)
        {
            writer.Append(TypeNameEmitter.EmitAsPatternType(tested));
        }

        writer.Append("(");

        for(var i = 0; i < tuple.Elements.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            var element = tuple.Elements[i];

            // At width zero this branch is unreachable; typed bindings avoid ITuple's nullable element contract.
            if(widthMissed && tested is null && element is CatchAllPattern { Binding: { } binding })
            {
                writer.Append($"object {LocalNames.Of(binding)}");
                continue;
            }

            if(hoisted is not null && EnumVariantTested(element) is { } variant)
            {
                var bound = TemporaryName();
                writer.Append($"var {bound}");
                hoisted.Add(new HoistedTest($"{bound} == {Render(variant, diagnostics)}"));
                continue;
            }

            EmitPattern(element, 0, hoisted, writer, diagnostics);
        }

        writer.Append(")");

        if(tuple.Binding is { } whole)
        {
            writer.Append($" {LocalNames.Of(whole)}");
        }

        if(widthMissed)
        {
            writer.Append(")");
        }
    }
}