using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// The type a declaration's own body gives a value of itself: a generic definition applied to its own
/// parameters, a pack filling its slot as the one spread the body writes it with, and no enclosing
/// instantiation, since a body names its own type without one (<see cref="ScopeResolver.InheritedEnclosing"/>).
/// Built at binding rather than answered by the type's scope because it has to be the interned construction
/// the body's own type references reach, and the model layer holds no interner.
/// </summary>
internal static class SelfConstruction
{
    public static IMistType Of(MemberType definition, TypeInterner interner)
    {
        var parameters = definition.TypeParametersList;

        if(parameters.Count == 0)
        {
            return definition;
        }

        var arguments = new IMistType[parameters.Count];

        for(var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = parameters[i].IsPack ? PackSlot(parameters[i], interner) : parameters[i];
        }

        return interner.GetOrCreateConstructedType(definition, arguments);
    }

    private static LiftedType PackSlot(TypeParameter pack, TypeInterner interner)
    {
        return interner.LiftPack(pack.Domain ?? pack, new TypePackValue([new SpreadType(pack)]));
    }
}