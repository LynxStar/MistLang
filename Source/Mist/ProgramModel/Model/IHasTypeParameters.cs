using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// The writable side of <see cref="IGenericDeclaration"/>: the list construction populates and
/// lookup reads, where the contract exposes a read-only view of it.
/// <para>
/// The list itself cannot be shared, since each declaration owns its own. What this shares is the
/// ability to ask any declaration for it without knowing which kind it is.
/// </para>
/// </summary>
internal interface IHasTypeParameters : IGenericDeclaration
{
    List<TypeParameter> TypeParametersList { get; }
}