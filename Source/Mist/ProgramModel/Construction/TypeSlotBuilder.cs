using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves associated type slot declarations into <see cref="TypeSlot"/> instances
/// and adds them to the owning abstract type.
/// </summary>
internal sealed class TypeSlotBuilder
{
    private readonly BuilderContext context;

    public TypeSlotBuilder(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Resolves a unified associated type slot declaration.
    /// </summary>
    public void Resolve(AssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;
        var modifiers = ModifierBuilder.Build(slotDecl, context.Diagnostics);

        var slot = new TypeSlot(name, [], modifiers);
        typeDef.TypeSlotsList.Add(slot);
    }
}