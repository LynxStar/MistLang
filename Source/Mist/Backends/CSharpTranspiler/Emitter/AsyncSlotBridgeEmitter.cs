using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class AsyncSlotBridgeEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var fillers = ConformanceAnalyzer.Fillers(type).ToList();
        var bridged = type.ConformancesList.SelectMany(conformance => conformance.Mappings.OfType<MethodMapping>())
            .Select(mapping => MethodEmitter.EmitSignature(mapping.Target, diagnostics,
                $"{TypeNameEmitter.Emit(mapping.Declarer)}.{IdentifierSpelling.Of(mapping.Target.Name)}"))
            .ToHashSet(StringComparer.Ordinal);

        foreach(var (reached, definition, substitution) in AbstractInheritance.Reached(SlotLift.Reached(type), interner: null))
        {
            var interfaceName = TypeNameEmitter.Emit(SlotLift.Base(reached, type) ?? reached);

            foreach(var slot in definition.MethodsList)
            {
                if(slot.Modifiers.IsStatic || slot.Modifiers.IsAsync
                    || SlotLift.Erased(slot.ReturnType) is not null
                    || PackSlotBridgeEmitter.Arguments(slot, substitution) is not null)
                {
                    continue;
                }

                var source = fillers.FirstOrDefault(filler => filler.Method is { } method && method.Name == slot.Name
                    && SlotSignatures.Answers(slot, method, substitution, filler.Substitution, interner: null))?.Method;

                if(!AsyncPromotion.IsPromoted(source))
                {
                    continue;
                }

                string signature;

                using(TypeNameEmitter.Substituting(substitution))
                {
                    signature = MethodEmitter.EmitSignature(slot, diagnostics, $"{interfaceName}.{IdentifierSpelling.Of(slot.Name)}");
                }

                if(!bridged.Add(signature))
                {
                    continue;
                }

                var typeParameters = MethodEmitter.ForwardingTypeArguments(slot, source);
                var arguments = MethodEmitter.ForwardingArguments(slot, source, SlotSignatures.Closing(slot, source!, substitution));

                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                writer.WriteLine($"{signature} => this.{IdentifierSpelling.Of(source!.Name)}{typeParameters}({arguments}).GetAwaiter().GetResult();");
            }
        }
    }
}