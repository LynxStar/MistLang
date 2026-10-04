using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Reconciles a member that fills a pack-typed slot with the signature the emitted interface declares
/// for it. The interface keeps the value pack as ONE tuple parameter at every width, which is what lets
/// a call through the abstract pack its arguments without knowing the implementer; a type that closes
/// the pack writes one parameter per element instead (<see cref="ValuePack.Expand"/>), so no member of
/// it has the interface's signature. An EXPLICIT implementation that unpacks the tuple into the public
/// member serves both, and keeps a same-named member that does take the tuple from filling the slot by
/// C#'s implicit rule where Mist's rule chose the other.
/// </summary>
internal static class PackSlotBridgeEmitter
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
                if(slot.Modifiers.IsStatic
                    || Arguments(slot, substitution) is not { } arguments
                    || fillers.FirstOrDefault(filler => filler.Method is { } method && method.Name == slot.Name
                        && SlotSignatures.Answers(slot, method, substitution, filler.Substitution, interner: null))?.Method is not { } source)
                {
                    continue;
                }

                var typeParameters = slot.TypeParametersList.Count > 0
                    ? $"<{string.Join(", ", slot.TypeParametersList.Select(DependentCarrierNames.Parameter))}>"
                    : "";
                string head;

                using(TypeNameEmitter.Substituting(substitution))
                {
                    head = $"{MethodEmitter.EmitReturnType(slot)} {interfaceName}.{slot.Name}{typeParameters}"
                        + $"({ParameterEmitter.Emit(slot.ParametersList, diagnostics)})";
                }

                if(bridged.Add(head))
                {
                    TypeEmitter.SeparateMember(writer, ref anyEmitted);
                    var completion = AsyncPromotion.IsPromoted(source) ? ".GetAwaiter().GetResult()" : "";
                    writer.WriteLine($"{head} => this.{source.Name}{typeParameters}({arguments}){completion};");
                }
            }
        }
    }

    internal static string? Arguments(Method slot, TypeSubstitution substitution)
    {
        var arguments = new List<string>();
        var unpacks = false;

        foreach(var parameter in slot.ParametersList)
        {
            if(ValuePack.PackParameterOf(parameter) is not { } pack
                || substitution.Apply(pack, interner: null) is not LiftedType { Value: TypePackValue bound }
                || bound.Elements is [SpreadType])
            {
                arguments.Add($"{ArgumentKeywords.For(parameter)}{parameter.Name}");
                continue;
            }

            unpacks = true;
            var item = 0;

            foreach(var element in bound.Elements)
            {
                if(element is not SpreadType spread)
                {
                    arguments.Add($"{parameter.Name}.Item{++item}");
                    continue;
                }

                if(MonomorphReference.Elements(TypeNameEmitter.Closed(spread.Operand)) is not { } spliced)
                {
                    return null;
                }

                var items = spliced.Select(_ => $"{parameter.Name}.Item{++item}").ToList();

                arguments.Add(items.Count switch
                {
                    0 => "default(System.ValueTuple)",
                    1 => $"System.ValueTuple.Create({items[0]})",
                    _ => $"({string.Join(", ", items)})",
                });
            }
        }

        return unpacks ? string.Join(", ", arguments) : null;
    }
}