using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The per-element members a spread declaration stands for: <c>public TArgs... Item;</c> exposes
/// the aggregate <c>Item</c> and one projection <c>Item1</c> .. <c>ItemN</c>.
/// <para>
/// PROJECTED RATHER THAN STORED, whichever kind the declaration used. The aggregate holds the
/// values and each element reads through it, so writing one is writing the other -- which is what
/// the declaration means and what independent backing fields would quietly break, since the
/// constructor fills only the aggregate. C# names a tuple's elements Item1..ItemN whatever the
/// aggregate is called, and does so at every width: the zero and one cases emit as ValueTuple
/// rather than as tuple syntax, which has those members too.
/// </para>
/// </summary>
internal static class SpreadProjectionEmitter
{
    public static void Emit(MemberType type, CSharpWriter writer, ref bool anyEmitted)
    {
        if(Monomorph.InScope() is not { } monomorph)
        {
            return;
        }

        foreach(var field in (type as ConcreteType)?.FieldsList ?? [])
        {
            EmitFamily(field.Name, field.Type, field.Modifiers, writable: true, monomorph, writer, ref anyEmitted);
        }

        foreach(var property in type.PropertiesList)
        {
            EmitFamily(
                property.Name, property.Type, property.Modifiers,
                writable: property.Setter is not null, monomorph, writer, ref anyEmitted);
        }
    }

    private static void EmitFamily(
        string stem,
        IMistType declaredType,
        MemberModifiers modifiers,
        bool writable,
        Monomorph monomorph,
        CSharpWriter writer,
        ref bool anyEmitted)
    {
        if(!SpreadMember.IsSpread(declaredType)
            || declaredType is not TupleType { ElementTypes: [SpreadType spread] }
            || spread.Operand is not TypeParameter pack
            || !monomorph.Binds(pack))
        {
            return;
        }

        var expansion = monomorph.Expansion(pack);

        var emitted = MemberModifierEmitter.Emit(modifiers, emitReadonly: false);

        for(var i = 0; i < expansion.Count; i++)
        {
            var element = TypeNameEmitter.Emit(expansion[i]);
            var slot = $"{stem}.Item{i + 1}";
            var accessors = writable ? $"get => {slot}; set => {slot} = value;" : $"get => {slot};";

            TypeEmitter.SeparateMember(writer, ref anyEmitted);
            writer.WriteLine($"{emitted}{element} {stem}{i + 1} {{ {accessors} }}");
        }
    }
}