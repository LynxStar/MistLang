using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The <c>Drop</c> a container never declared, releasing each droppable field in declaration order.
/// See <see cref="DropInsertion.SynthesizesDrop"/> for which types get one and why writing none is not
/// the author declining.
/// </summary>
internal static class DropEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, ref bool anyEmitted)
    {
        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;

        writer.WriteLine("public void Drop()");

        using(writer.Block())
        {
            foreach(var field in DropInsertion.DroppableFields(type))
            {
                writer.WriteLine(DropInsertion.FieldDrop(field));
            }
        }
    }
}