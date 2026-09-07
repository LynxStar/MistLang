namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// One element of a pack at a known index, where the pack's ARITY is fixed but its element types
/// are not -- <c>TShapes[0]</c> inside <c>ShapePair&lt;Type[2]:TShapes&gt;</c>.
/// <para>
/// A type parameter, which is what carries the whole-pack constraint down to it: a
/// <c>where TShapes : IShape</c> holds of every element, so seeding this with the pack's own
/// constraints is what makes <c>Shape1.Area()</c> resolve. Being a parameter rather than the pack
/// is equally load-bearing in the other direction -- the pack is a <c>Type[]</c> and would answer
/// for <c>Length</c>, which one element must not.
/// </para>
/// </summary>
internal sealed class PackElementType : TypeParameter
{
    public PackElementType(TypeParameter pack, int index)
        : base($"{pack.Name}[{index}]", index, pack.Scope, pack.DeclaringType)
    {
        Pack = pack;
        Index = index;
        ConstraintsList.AddRange(pack.ConstraintsList);
    }

    public TypeParameter Pack { get; }

    public int Index { get; }
}