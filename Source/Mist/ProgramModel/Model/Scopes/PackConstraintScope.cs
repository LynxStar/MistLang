using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// The scope a pack's own <c>where</c> constraints are resolved in. A whole-pack constraint holds of
/// each element on its own (variadics Requirement 11.1), so inside one the pack's name is the element
/// under judgment -- <c>where Ts : IRank of Ts</c> asks IRank of Score of a Score -- and is one type
/// there, where anywhere else it is the list. It declares nothing; it exists to be found on the chain.
/// </summary>
internal sealed class PackConstraintScope(Scope parent, TypeParameter pack) : Scope(parent)
{
    private TypeParameter Pack { get; } = pack;

    public static bool Constrains(Scope? scope, TypeParameter pack)
    {
        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is PackConstraintScope constraint && ReferenceEquals(constraint.Pack, pack))
            {
                return true;
            }
        }

        return false;
    }
}