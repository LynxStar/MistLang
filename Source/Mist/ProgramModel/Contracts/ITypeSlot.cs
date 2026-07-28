using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An associated type slot declared on an abstract type. A slot is a more
/// specific kind of <see cref="ITypeParameter"/>: it is closed by conformance
/// (an implementing type supplies the concrete type) rather than by a type
/// argument supplied at the use site, and — unlike a plain type parameter — it
/// may declare its own type parameters.
///
/// The slot's own constraints (the <c>:</c> bound) live on the inherited
/// <see cref="ITypeParameter.Constraints"/>; constraints on the slot's own type
/// parameters live on those parameters.
/// </summary>
public interface ITypeSlot : ITypeParameter, IGenericDeclaration
{
}