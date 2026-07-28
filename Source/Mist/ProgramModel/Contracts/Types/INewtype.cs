namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A newtype definition.
/// </summary>
public interface INewtype : IMistType, IGenericDeclaration
{
    /// <summary>
    /// The source type this newtype wraps (resolved from the <c>new T</c> annotation).
    /// </summary>
    IMistType? SourceType { get; }
}