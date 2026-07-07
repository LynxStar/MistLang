namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A union type composed of multiple constituent types (e.g., <c>int | string</c>).
/// </summary>
public interface IUnionType : IMistType
{
    /// <summary>
    /// The resolved constituent types of the union.
    /// </summary>
    IReadOnlyList<IMistType> Constituents { get; }
}