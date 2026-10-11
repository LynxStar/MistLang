using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// C# lets a generic member implement an interface's only when their constraints are identical, while Mist
/// admits an answer that requires less than its slot promises. Such an answer keeps its own constraints for
/// direct calls, and an EXPLICIT implementation, which inherits the interface's, forwards to it. An answer
/// whose default or constructor witnesses differ from the slot's has no forwarding spelled here.
/// </summary>
internal static class ConstraintSlotBridgeEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var fillers = ConformanceAnalyzer.Fillers(type).ToList();
        var bridged = new HashSet<string>(StringComparer.Ordinal);

        foreach(var (reached, definition, substitution) in AbstractInheritance.Reached(SlotLift.Reached(type), interner: null))
        {
            var interfaceName = TypeNameEmitter.Emit(SlotLift.Base(reached, type) ?? reached);

            foreach(var slot in definition.MethodsList)
            {
                if(slot.Modifiers.IsStatic || slot.TypeParametersList.Count == 0
                    || SlotLift.Erased(slot.ReturnType) is not null
                    || PackSlotBridgeEmitter.Arguments(slot, substitution) is not null)
                {
                    continue;
                }

                var filler = fillers.FirstOrDefault(filler => filler.Method is { } method && method.Name == slot.Name
                    && SlotSignatures.Answers(slot, method, substitution, filler.Substitution, interner: null));

                if(filler?.Method is not { } source || AsyncPromotion.IsPromoted(source))
                {
                    continue;
                }

                var renaming = SlotSignatures.Closing(slot, source, substitution);

                if(SameConstraints(slot, source, renaming, filler.Substitution) || !SameWitnesses(slot, source, renaming))
                {
                    continue;
                }

                string signature;

                using(TypeNameEmitter.Substituting(substitution))
                {
                    signature = MethodEmitter.EmitSignature(slot, diagnostics, $"{interfaceName}.{slot.Name}");
                }

                if(!bridged.Add(signature))
                {
                    continue;
                }

                var reference = slot.Modifiers.IsRef ? "ref " : "";

                TypeEmitter.SeparateMember(writer, ref anyEmitted);
                writer.WriteLine(
                    $"{signature} => {reference}this.{source.Name}{MethodEmitter.ForwardingTypeArguments(slot)}({MethodEmitter.ForwardingArguments(slot)});");
            }
        }
    }

    private static bool SameConstraints(Method slot, Method answer, TypeSubstitution renaming, TypeSubstitution inherited)
    {
        for(var i = 0; i < slot.TypeParametersList.Count && i < answer.TypeParametersList.Count; i++)
        {
            List<string> promised;
            List<string> required;

            using(TypeNameEmitter.Substituting(renaming))
            {
                promised = ConstraintEmitter.Parts(slot.TypeParametersList[i], liftSlots: true);
            }

            using(TypeNameEmitter.Substituting(inherited))
            {
                required = ConstraintEmitter.Parts(answer.TypeParametersList[i], liftSlots: true);
            }

            if(!promised.ToHashSet(StringComparer.Ordinal).SetEquals(required))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameWitnesses(Method slot, Method answer, TypeSubstitution renaming)
    {
        for(var i = 0; i < slot.TypeParametersList.Count && i < answer.TypeParametersList.Count; i++)
        {
            var (promised, required) = (slot.TypeParametersList[i], answer.TypeParametersList[i]);

            if(DefaultConstraintWitness.Constrains(promised) != DefaultConstraintWitness.Constrains(required)
                || !Signatures(promised, renaming).SequenceEqual(Signatures(required, TypeSubstitution.Empty), StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> Signatures(TypeParameter parameter, TypeSubstitution substitution)
    {
        return parameter.ConstraintsList.OfType<ConstructorConstraint>()
            .Select(constructor => string.Join(",", constructor.ParameterTypes.Select(type => TypeNameEmitter.Emit(substitution.Apply(type, interner: null)))));
    }
}