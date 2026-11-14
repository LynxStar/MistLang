using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An associated type slot on an abstract. Unlike a plain <see cref="ITypeParameter"/> it is closed by
/// conformance rather than by a use-site type argument, and it may declare type parameters of its own.
/// <para>
/// The slot's own <c>:</c> bound lives on <see cref="ITypeParameter.Constraints"/>; constraints on its
/// type parameters live on those parameters.
/// </para>
/// </summary>
public interface ITypeSlot : ITypeParameter, IGenericDeclaration
{
}