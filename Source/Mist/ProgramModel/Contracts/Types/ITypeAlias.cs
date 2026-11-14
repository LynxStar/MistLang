namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Includes refinement types.
/// </summary>
public interface ITypeAlias : IMistType, IGenericDeclaration
{
    IMistType? AliasedType { get; }
}