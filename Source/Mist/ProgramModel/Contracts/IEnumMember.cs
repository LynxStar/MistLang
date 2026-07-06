namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A single member (variant) of an enum type.
/// </summary>
public interface IEnumMember
{
    /// <summary>The variant name.</summary>
    string Name { get; }

    /// <summary>Whether this variant is marked as the enum's default.</summary>
    bool IsDefault { get; }
}