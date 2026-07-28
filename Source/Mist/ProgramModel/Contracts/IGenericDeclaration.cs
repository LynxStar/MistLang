using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A declaration that may introduce type parameters of its own.
/// <para>
/// Implemented across four unrelated branches -- a type, a type alias or newtype, an extension
/// block, a method, and an associated-type slot -- so there is no base class that could carry it.
/// </para>
/// </summary>
public interface IGenericDeclaration
{
    /// <summary>
    /// The type parameters declared here. Empty when the declaration is not generic.
    /// </summary>
    IReadOnlyList<ITypeParameter> TypeParameters { get; }
}