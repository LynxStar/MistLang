using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An interface because its implementers -- types, aliases and newtypes, extension blocks, methods and
/// associated-type slots -- share no base class that could carry it.
/// </summary>
public interface IGenericDeclaration
{
    IReadOnlyList<ITypeParameter> TypeParameters { get; }
}