namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// An enum type definition.
/// </summary>
public interface IEnumType : IMistType, IHasAttributes
{
    /// <summary>
    /// The resolved underlying type (from explicit type annotation), or null
    /// if no annotation was provided (type will be inferred later).
    /// </summary>
    IMistType? UnderlyingType { get; }

    /// <summary>
    /// The declared enum members (variants).
    /// </summary>
    IReadOnlyList<IEnumMember> Members { get; }
}