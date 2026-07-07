namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A type alias definition (includes refinement types).
/// </summary>
public interface ITypeAlias : IMistType
{
    /// <summary>
    /// The resolved type this alias refers to.
    /// </summary>
    IMistType? AliasedType { get; }
}