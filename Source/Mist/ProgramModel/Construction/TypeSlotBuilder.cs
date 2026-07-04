using Mist.Common.Diagnostics;
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
    /// Resolves a constrained associated type slot declaration.
    /// </summary>
    public void Resolve(AssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;

        // Constraint resolution is not yet implemented.
        var slot = new TypeSlot(name, [], default);
        typeDef.TypeSlotsList.Add(slot);
    }

    /// <summary>
    /// Resolves an unconstrained associated type slot declaration.
    /// </summary>
    public static void ResolveUnconstrained(UnconstrainedAssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;

        var slot = new TypeSlot(name, [], default);
        typeDef.TypeSlotsList.Add(slot);
    }

    /// <summary>
    /// Resolves a modified associated type slot declaration (has modifiers and optional constraints).
    /// </summary>
    public void ResolveModified(ModifiedAssociatedTypeSlotDeclarationNode slotDecl, AbstractType typeDef)
    {
        var name = slotDecl.Name.Lexeme;
        var modifiers = ModifierBuilder.Build(slotDecl.Modifiers, context.Diagnostics, slotDecl);

        // Constraint resolution is not yet implemented.
        var slot = new TypeSlot(name, [], modifiers);
        typeDef.TypeSlotsList.Add(slot);
    }
}
